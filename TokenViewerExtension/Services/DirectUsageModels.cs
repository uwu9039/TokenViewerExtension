// TokenViewerExtension contributors. Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace TokenViewerExtension;

/// <summary>某天（官方平台数据）的用量。</summary>
internal sealed record DirectDayUsage(string Day, long Input, long Output, long Cached, long Requests, double? Cost);

/// <summary>某模型（官方平台数据，当月累计）的用量。</summary>
internal sealed record DirectModelUsage(string Model, long Input, long Output, long Cached, long Requests);

/// <summary>某个直连平台（DeepSeek 官方平台 / OpenAI / Anthropic）的用量快照。</summary>
internal sealed class DirectPlatformSnapshot
{
    public DateTime FetchedAt { get; set; }

    public List<DirectDayUsage> Days { get; set; } = [];

    public List<DirectModelUsage> Models { get; set; } = [];

    /// <summary>当月消费金额（官方数据，可能为空）。</summary>
    public double? TotalCost { get; set; }

    /// <summary>余额显示文本（来自官方平台）。</summary>
    public string? BalanceText { get; set; }

    /// <summary>拉取失败时的错误信息。</summary>
    public string? Error { get; set; }

    /// <summary>出错时附带的原始响应片段（调试用，帮助定位平台接口变更）。</summary>
    public string? RawResponse { get; set; }

    /// <summary>按天数据是否有值（官方按天统计可能延迟，此时以月度汇总为准）。</summary>
    public bool DaysHaveData
    {
        get
        {
            long sum = 0;
            foreach (var day in Days)
            {
                sum += day.Input + day.Output + day.Cached;
            }

            return sum != 0;
        }
    }

    /// <summary>本月总 token（按天有数据用按天，否则用按模型汇总）。</summary>
    public long TotalTokens => DaysHaveData ? SumDays(d => d.Input + d.Output + d.Cached) : SumModels(m => m.Input + m.Output + m.Cached);

    public long MonthInput => DaysHaveData ? SumDays(d => d.Input) : SumModels(m => m.Input);

    public long MonthOutput => DaysHaveData ? SumDays(d => d.Output) : SumModels(m => m.Output);

    public long MonthCached => DaysHaveData ? SumDays(d => d.Cached) : SumModels(m => m.Cached);

    public long MonthRequests => DaysHaveData ? SumDays(d => d.Requests) : SumModels(m => m.Requests);

    private long SumDays(Func<DirectDayUsage, long> selector)
    {
        long sum = 0;
        foreach (var day in Days)
        {
            sum += selector(day);
        }

        return sum;
    }

    private long SumModels(Func<DirectModelUsage, long> selector)
    {
        long sum = 0;
        foreach (var model in Models)
        {
            sum += selector(model);
        }

        return sum;
    }
}

/// <summary>直连平台的定义（配置了对应密钥才会出现在列表里）。</summary>
internal sealed record DirectPlatformInfo(string Id, string DisplayName, string Icon, string ConsoleUrl);

/// <summary>JSON 解析小工具（全部基于 JsonDocument，AOT 安全）。</summary>
internal static class JsonHelpers
{
    /// <summary>取整数字段（支持数字或数字字符串）。</summary>
    public static long GetLong(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number)
                {
                    return prop.GetInt64();
                }

                if (prop.ValueKind == JsonValueKind.String && long.TryParse(prop.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                {
                    return value;
                }
            }
        }

        return 0;
    }

    /// <summary>取小数（支持数字或数字字符串）。</summary>
    public static double? GetDouble(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number)
                {
                    return prop.GetDouble();
                }

                if (prop.ValueKind == JsonValueKind.String && double.TryParse(prop.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    return value;
                }
            }
        }

        return null;
    }
}

/// <summary>官方数据聚合工具：把接口返回的明细合并为按天、按模型的累计。</summary>
internal static class UsageAggregator
{
    public static void AddDay(Dictionary<string, DirectDayUsage> map, string day, long input, long output, long cached, long requests, double? cost = null)
    {
        if (map.TryGetValue(day, out var existing))
        {
            map[day] = existing with
            {
                Input = existing.Input + input,
                Output = existing.Output + output,
                Cached = existing.Cached + cached,
                Requests = existing.Requests + requests,
                Cost = MergeCost(existing.Cost, cost),
            };
        }
        else
        {
            map[day] = new DirectDayUsage(day, input, output, cached, requests, cost);
        }
    }

    public static void AddModel(Dictionary<string, DirectModelUsage> map, string model, long input, long output, long cached, long requests)
    {
        if (map.TryGetValue(model, out var existing))
        {
            map[model] = existing with
            {
                Input = existing.Input + input,
                Output = existing.Output + output,
                Cached = existing.Cached + cached,
                Requests = existing.Requests + requests,
            };
        }
        else
        {
            map[model] = new DirectModelUsage(model, input, output, cached, requests);
        }
    }

    private static double? MergeCost(double? left, double? right)
    {
        return (left, right) switch
        {
            (null, null) => null,
            (null, var r) => r,
            (var l, null) => l,
            (var l, var r) => l + r,
        };
    }

    /// <summary>按天降序排序。</summary>
    public static List<DirectDayUsage> SortDays(Dictionary<string, DirectDayUsage> days)
    {
        var list = new List<DirectDayUsage>(days.Values);
        list.Sort((a, b) => string.CompareOrdinal(b.Day, a.Day));
        return list;
    }

    /// <summary>按总 token 降序排序。</summary>
    public static List<DirectModelUsage> SortModels(Dictionary<string, DirectModelUsage> models)
    {
        var list = new List<DirectModelUsage>(models.Values);
        list.Sort((a, b) => (b.Input + b.Output + b.Cached).CompareTo(a.Input + a.Output + a.Cached));
        return list;
    }
}
