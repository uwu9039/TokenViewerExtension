// TokenViewerExtension contributors. Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TokenViewerExtension;

/// <summary>
/// DeepSeek 官方平台数据直连（同步平台监控页显示的数据）。
/// 这些是平台前端内部接口（非公开），需要网页登录 token（localStorage.userToken）而非 API Key，
/// 可能随时变更，所有解析均为防御式并带错误提示。
/// 接口：
///   GET platform.deepseek.com/api/v0/usage/amount?month=&amp;year=  按天+按模型 token 用量
///   GET platform.deepseek.com/api/v0/usage/cost?month=&amp;year=    消费金额（尽力解析，失败不报错）
///   GET platform.deepseek.com/api/v0/users/get_user_summary        平台余额（尽力解析）
/// </summary>
internal static class DeepSeekPlatformClient
{
    // 可注入以便测试（真实环境使用官方地址）
    internal static string BaseUrl = "https://platform.deepseek.com";

    public static async Task<DirectPlatformSnapshot?> FetchAsync(string userToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userToken))
        {
            return null;
        }

        try
        {
            var now = DateTime.Now;
            var amountUrl = $"{BaseUrl}/api/v0/usage/amount?month={now.Month}&year={now.Year}";

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var (snapshot, statusCode, raw) = await FetchAmountAsync(client, amountUrl, userToken, cancellationToken);
            if (snapshot is not null)
            {
                // 消费金额与余额为尽力而为，失败不影响主数据
                snapshot.TotalCost = await FetchCostAsync(client, userToken, now, cancellationToken);
                snapshot.BalanceText = await FetchSummaryAsync(client, userToken, cancellationToken);
                return snapshot;
            }

            var rawDetail = raw is not null ? $" 原始响应：{raw}" : string.Empty;
            return new DirectPlatformSnapshot
            {
                Error = statusCode is null
                    ? $"DeepSeek 平台接口无响应（网络错误或接口已变更）{rawDetail}"
                    : $"DeepSeek 平台接口返回 {statusCode}（Token 无效、已过期或接口变更，请重新从浏览器复制）{rawDetail}",
            };
        }
        catch (Exception e)
        {
            return new DirectPlatformSnapshot { Error = $"DeepSeek 平台查询失败：{e.Message}" };
        }
    }

    /// <summary>
    /// 拉取用量数据并带回原始响应（成功/失败都捕获，用于排查「0 tokens」类问题）。
    /// 重试策略：HTTP 4xx 或解析失败时，去掉 x-app-version 头重试一次（部分环境会拒绝该头）。
    /// </summary>
    private static async Task<(DirectPlatformSnapshot? Snapshot, int? StatusCode, string? Raw)> FetchAmountAsync(HttpClient client, string url, string userToken, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = BuildRequest(HttpMethod.Get, url, userToken, includeAppVersion: attempt == 0);
            using var response = await client.SendAsync(request, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var snapshot = ParseAmount(raw);
                // 成功也捕获原始响应（截断到 4000 字符），供「查看原始响应」排查 0 tokens
                snapshot.RawResponse = Truncate(raw, 4000);
                if (snapshot.Error is null)
                {
                    return (snapshot, null, null);
                }

                if (attempt == 0)
                {
                    continue; // 解析失败：换请求头重试一次
                }

                return (snapshot, null, null);
            }

            var status = (int)response.StatusCode;
            if (status is < 400 or >= 500)
            {
                return (null, status, Truncate(raw, 500)); // 5xx 重试无意义
            }

            // 4xx：换请求头再试一次
        }

        return (null, 400, null);
    }

    /// <summary>截断原始响应，避免诊断信息过长。</summary>
    private static string Truncate(string text, int maxLength)
    {
        return text.Length > maxLength ? text[..maxLength] + "…" : text;
    }

    private static HttpRequestMessage BuildRequest(HttpMethod method, string url, string userToken, bool includeAppVersion)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        // 内部接口对客户端环境敏感，伪装成浏览器请求
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
        if (includeAppVersion)
        {
            request.Headers.TryAddWithoutValidation("x-app-version", "1.0.0");
        }

        return request;
    }

    /// <summary>解析用量接口：biz_data.total（按模型）+ biz_data.days（按天）。</summary>
    internal static DirectPlatformSnapshot ParseAmount(string json)
    {
        var snapshot = new DirectPlatformSnapshot { FetchedAt = DateTime.Now };
        var days = new Dictionary<string, DirectDayUsage>();
        var models = new Dictionary<string, DirectModelUsage>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // 错误信封：{"code":40002,"msg":"Missing Token","data":null}（认证失败返回 HTTP 200）
            if (root.TryGetProperty("code", out var codeProp) && codeProp.ValueKind == JsonValueKind.Number && codeProp.GetInt64() != 0)
            {
                var msg = root.TryGetProperty("msg", out var msgProp) ? msgProp.GetString() : null;
                snapshot.Error = $"DeepSeek 平台返回错误：{msg ?? $"code {codeProp.GetInt64()}"}（Token 无效、已过期或需重新登录）";
                return snapshot;
            }

            var biz = FindBizData(root);
            if (biz.ValueKind == JsonValueKind.Array && biz.GetArrayLength() > 0)
            {
                biz = biz[0]; // 防御：有些接口把 biz_data 放数组里
            }

            if (biz.ValueKind != JsonValueKind.Object)
            {
                snapshot.Error = "DeepSeek 平台响应格式异常（接口可能已变更）";
                return snapshot;
            }

            // 按天明细（优先）：days 与 total 是同一数据的两种聚合
            var hasDays = false;
            if (biz.TryGetProperty("days", out var dayArray) && dayArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var day in dayArray.EnumerateArray())
                {
                    var date = day.TryGetProperty("date", out var dateProp) ? dateProp.GetString() : null;
                    if (string.IsNullOrWhiteSpace(date))
                    {
                        continue;
                    }

                    if (!day.TryGetProperty("data", out var dayData) || dayData.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var modelCell in dayData.EnumerateArray())
                    {
                        var (input, output, cached, requests) = ParseUsageCell(modelCell, out var model);
                        UsageAggregator.AddDay(days, date, input, output, cached, requests);
                        UsageAggregator.AddModel(models, model, input, output, cached, requests);
                        hasDays = true;
                    }
                }
            }

            // days 为空或全 0 时，退回当月按模型汇总（total）——两者是同一数据，只取其一避免翻倍
            var daysTotal = 0L;
            foreach (var day in days.Values)
            {
                daysTotal += day.Input + day.Output + day.Cached;
            }

            if ((!hasDays || daysTotal == 0) && biz.TryGetProperty("total", out var total) && total.ValueKind == JsonValueKind.Array)
            {
                foreach (var modelCell in total.EnumerateArray())
                {
                    var (input, output, cached, requests) = ParseUsageCell(modelCell, out var model);
                    UsageAggregator.AddModel(models, model, input, output, cached, requests);
                }
            }
        }
        catch (JsonException)
        {
            // 认证失败时平台可能返回 HTML 错误页（HTTP 200），提示用户重新获取 Token
            snapshot.Error = "DeepSeek 平台返回了非 JSON 内容（Token 无效、已过期或接口变更，请重新获取 Token）";
            return snapshot;
        }

        snapshot.Days = UsageAggregator.SortDays(days);
        snapshot.Models = UsageAggregator.SortModels(models);
        return snapshot;
    }

    /// <summary>
    /// 定位业务数据：平台响应存在多层包装（实测为 data.biz_data），
    /// biz_data 可能在根级（老结构）、data.biz_data（当前结构）或 data 直接携带。
    /// </summary>
    private static JsonElement FindBizData(JsonElement root)
    {
        if (root.TryGetProperty("biz_data", out var direct) && direct.ValueKind == JsonValueKind.Object)
        {
            return direct;
        }

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            if (data.TryGetProperty("biz_data", out var nested) && nested.ValueKind == JsonValueKind.Object)
            {
                return nested;
            }

            return data;
        }

        return root;
    }

    /// <summary>
    /// 解析一个「模型 + usage 数组」的单元格。usage 元素形如
    /// { "type": "PROMPT_CACHE_HIT_TOKEN", "amount": "123" }，amount 为数字字符串；
    /// 接口字段名可能变化，未知 type 按名称特征兜底映射，数值字段兼容 amount/value/count/num。
    /// </summary>
    private static (long Input, long Output, long Cached, long Requests) ParseUsageCell(JsonElement cell, out string model)
    {
        model = cell.TryGetProperty("model", out var modelProp) ? modelProp.GetString() ?? "未知模型" : "未知模型";
        long hit = 0, miss = 0, prompt = 0, response = 0, requests = 0;
        if (cell.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in usage.EnumerateArray())
            {
                var type = item.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;
                var amount = JsonHelpers.GetLong(item, "amount", "value", "count", "num");
                switch (type)
                {
                    case "PROMPT_CACHE_HIT_TOKEN":
                        hit = amount;
                        break;
                    case "PROMPT_CACHE_MISS_TOKEN":
                        miss = amount;
                        break;
                    case "PROMPT_TOKEN":
                        prompt = amount;
                        break;
                    case "RESPONSE_TOKEN":
                        response = amount;
                        break;
                    case "REQUEST":
                        requests = amount;
                        break;
                    default:
                        // 接口字段名可能变化：按名称特征兜底映射
                        if (type is not null)
                        {
                            var t = type.ToUpperInvariant();
                            if (t.Contains("MISS"))
                            {
                                miss += amount;
                            }
                            else if (t.Contains("HIT"))
                            {
                                hit += amount;
                            }
                            else if (t.Contains("RESPONSE") || t.Contains("OUTPUT") || t.Contains("COMPLETION"))
                            {
                                response += amount;
                            }
                            else if (t.Contains("REQUEST"))
                            {
                                requests += amount;
                            }
                            else if (t.Contains("CACHE"))
                            {
                                hit += amount;
                            }
                            else if (t.Contains("PROMPT") || t.Contains("INPUT"))
                            {
                                prompt += amount;
                            }
                        }

                        break;
                }
            }
        }

        // 部分口径只有 PROMPT_TOKEN，用 总输入 - 缓存命中 推算未命中部分
        if (miss == 0 && prompt > 0)
        {
            miss = Math.Max(0, prompt - hit);
        }

        return (miss, response, hit, requests);
    }

    /// <summary>消费金额（尽力解析，失败返回 null 不报错）。</summary>
    internal static double? ParseCost(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var biz = FindBizData(root);
            if (biz.ValueKind == JsonValueKind.Array && biz.GetArrayLength() > 0)
            {
                biz = biz[0];
            }

            if (biz.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // 常见形态：total 为数字 / 数字字符串 / 对象数组
            if (biz.TryGetProperty("total", out var total))
            {
                if (total.ValueKind == JsonValueKind.Number)
                {
                    return total.GetDouble();
                }

                if (total.ValueKind == JsonValueKind.String && double.TryParse(total.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    return value;
                }

                if (total.ValueKind == JsonValueKind.Array && total.GetArrayLength() > 0)
                {
                    var first = total[0];
                    if (JsonHelpers.GetDouble(first, "amount", "cost", "total", "value") is { } itemValue)
                    {
                        return itemValue;
                    }
                }
            }

            return JsonHelpers.GetDouble(biz, "total_cost", "total_amount", "amount", "cost");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>平台余额（尽力解析，失败返回 null 不报错）。</summary>
    internal static string? ParseSummary(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            foreach (var container in new[] { root, root.TryGetProperty("data", out var data) ? data : default })
            {
                if (container.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var name in new[] { "balance", "total_balance", "amount" })
                {
                    if (container.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.Number or JsonValueKind.String)
                    {
                        var text = value.ValueKind == JsonValueKind.Number
                            ? value.GetDouble().ToString("0.##", CultureInfo.InvariantCulture)
                            : value.GetString();
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            continue;
                        }

                        var currency = container.TryGetProperty("currency", out var currencyProp) ? currencyProp.GetString() : null;
                        return string.IsNullOrEmpty(currency) ? text : $"{currency} {text}";
                    }
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static async Task<double?> FetchCostAsync(HttpClient client, string userToken, DateTime now, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{BaseUrl}/api/v0/usage/cost?month={now.Month}&year={now.Year}";
            using var request = BuildRequest(HttpMethod.Get, url, userToken, includeAppVersion: true);
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return ParseCost(await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> FetchSummaryAsync(HttpClient client, string userToken, CancellationToken cancellationToken)
    {
        try
        {
            using var request = BuildRequest(HttpMethod.Get, $"{BaseUrl}/api/v0/users/get_user_summary", userToken, includeAppVersion: true);
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return ParseSummary(await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch
        {
            return null;
        }
    }
}
