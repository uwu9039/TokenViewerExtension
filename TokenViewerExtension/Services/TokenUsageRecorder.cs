// TokenViewerExtension contributors. Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TokenViewerExtension;

/// <summary>某模型今日的累计用量。</summary>
internal readonly record struct ModelUsage(string Model, long Input, long Output, long Cached, long Requests, bool HasEstimates);

/// <summary>某天的累计用量。</summary>
internal readonly record struct DayUsage(string Day, long Input, long Output, long Cached, long Requests);

/// <summary>用量汇总：负责把代理记录的用量聚合为页面需要的形式，并发出变更通知。</summary>
internal sealed class TokenUsageRecorder
{
    private readonly UsageStore _store;

    /// <summary>有新的用量记录时触发（来自代理工作线程）。</summary>
    public event Action? UsageChanged;

    public TokenUsageRecorder(UsageStore store)
    {
        _store = store;
    }

    /// <summary>记录一次成功请求的用量。</summary>
    public void Record(string providerId, string model, long input, long output, long cached, bool hasEstimates)
    {
        _store.Add(
            providerId,
            string.IsNullOrWhiteSpace(model) ? "未知模型" : model,
            DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            input,
            output,
            cached,
            requests: 1,
            hasEstimates);
        UsageChanged?.Invoke();
    }

    /// <summary>今日总量；providerId 为 null 时统计所有提供商。</summary>
    public (long Input, long Output, long Cached, long Requests, bool HasEstimates) GetTodayTotals(string? providerId)
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        long input = 0, output = 0, cached = 0, requests = 0;
        var hasEstimates = false;
        foreach (var record in _store.GetRecords())
        {
            if (record.Day != today)
            {
                continue;
            }

            if (providerId is not null && record.ProviderId != providerId)
            {
                continue;
            }

            input += record.InputTokens;
            output += record.OutputTokens;
            cached += record.CachedTokens;
            requests += record.Requests;
            hasEstimates |= record.HasEstimates;
        }

        return (input, output, cached, requests, hasEstimates);
    }

    /// <summary>某提供商今日的按模型分解（按总 token 降序）。</summary>
    public List<ModelUsage> GetTodayModelUsage(string providerId)
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var map = new Dictionary<string, ModelUsage>();
        foreach (var record in _store.GetRecords())
        {
            if (record.ProviderId != providerId || record.Day != today)
            {
                continue;
            }

            if (map.TryGetValue(record.Model, out var existing))
            {
                map[record.Model] = existing with
                {
                    Input = existing.Input + record.InputTokens,
                    Output = existing.Output + record.OutputTokens,
                    Cached = existing.Cached + record.CachedTokens,
                    Requests = existing.Requests + record.Requests,
                    HasEstimates = existing.HasEstimates || record.HasEstimates,
                };
            }
            else
            {
                map[record.Model] = new ModelUsage(record.Model, record.InputTokens, record.OutputTokens, record.CachedTokens, record.Requests, record.HasEstimates);
            }
        }

        return [.. map.Values.OrderByDescending(m => m.Input + m.Output + m.Cached)];
    }

    /// <summary>某提供商最近 N 天（有数据的日期，按日期降序）。</summary>
    public List<DayUsage> GetLastDays(string providerId, int days)
    {
        var map = new Dictionary<string, DayUsage>();
        foreach (var record in _store.GetRecords())
        {
            if (record.ProviderId != providerId)
            {
                continue;
            }

            if (map.TryGetValue(record.Day, out var existing))
            {
                map[record.Day] = existing with
                {
                    Input = existing.Input + record.InputTokens,
                    Output = existing.Output + record.OutputTokens,
                    Cached = existing.Cached + record.CachedTokens,
                    Requests = existing.Requests + record.Requests,
                };
            }
            else
            {
                map[record.Day] = new DayUsage(record.Day, record.InputTokens, record.OutputTokens, record.CachedTokens, record.Requests);
            }
        }

        return [.. map.Values.OrderByDescending(d => d.Day).Take(days)];
    }
}
