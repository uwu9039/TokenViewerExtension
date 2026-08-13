// TokenViewerExtension contributors. Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TokenViewerExtension;

/// <summary>
/// 直连模式服务：聚合 OpenAI / Anthropic 官方用量 API 与 DeepSeek 官方平台内部接口，
/// 提供与官方监控平台一致的数据。每 5 分钟自动刷新。
/// </summary>
#pragma warning disable CA1001 // 生命周期与扩展进程一致，无需手动释放
internal sealed class DirectUsageService
{
    // 平台内部接口建议克制调用（官方页本身约 5 分钟延迟）
    private static readonly TimeSpan MinFetchInterval = TimeSpan.FromMinutes(3);

    private readonly SettingsManager _settings;
    private readonly Dictionary<string, DirectPlatformSnapshot> _snapshots = [];
    private readonly Dictionary<string, DateTime> _lastFetch = [];
    private readonly object _lock = new();
    private readonly System.Timers.Timer _timer;

    /// <summary>任一平台数据更新时触发。</summary>
    public event Action? DataChanged;

    public DirectUsageService(SettingsManager settings)
    {
        _settings = settings;
        _timer = new System.Timers.Timer(5 * 60 * 1000) { AutoReset = true };
        _timer.Elapsed += (_, _) => RefreshAll();
        _timer.Start();
    }

    /// <summary>当前已配置（填了 Token）的直连平台列表（已精简为仅 DeepSeek）。</summary>
    public List<DirectPlatformInfo> Platforms
    {
        get
        {
            var list = new List<DirectPlatformInfo>();
            if (!string.IsNullOrWhiteSpace(_settings.DeepSeekPlatformToken))
            {
                list.Add(GetPlatformInfo("deepseek-platform"));
            }

            return list;
        }
    }

    public DirectPlatformSnapshot? GetSnapshot(string platformId)
    {
        lock (_lock)
        {
            return _snapshots.TryGetValue(platformId, out var snapshot) ? snapshot : null;
        }
    }

    /// <summary>该直连平台是否已配置密钥（用于页面显示配置指引）。</summary>
    public bool IsConfigured(string platformId)
    {
        return !string.IsNullOrWhiteSpace(GetToken(platformId));
    }

    /// <summary>固定平台定义（未配置 Token 时也可用于图表页等占位）。</summary>
    public static DirectPlatformInfo GetPlatformInfo(string platformId)
    {
        return platformId == "deepseek-platform"
            ? new DirectPlatformInfo("deepseek-platform", "DeepSeek 官方平台", "🐳", "https://platform.deepseek.com/usage")
            : new DirectPlatformInfo(platformId, platformId, "❓", string.Empty);
    }

    /// <summary>
    /// 官方「今日」数据（官方按天数据延迟时退回月度汇总）。
    /// 官方按天数据可能延迟（全 0），此时退回月度汇总并标注「本月」。
    /// </summary>
    public (long Input, long Output, long Cached, long Requests, bool IsOfficial, string? DayLabel) GetTodayForProvider(string providerId)
    {
        if (providerId != "deepseek")
        {
            return default;
        }

        var snapshot = GetSnapshot("deepseek-platform");
        if (snapshot is null || snapshot.Error is not null || snapshot.Days.Count == 0)
        {
            return default;
        }

        var today = DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var day = snapshot.Days.FirstOrDefault(d => d.Day == today);
        if (day is null)
        {
            day = snapshot.Days[0];
        }

        // 按天数据未更新（全 0）但有月度汇总：退回月度数据并标注「本月」
        if (day.Input + day.Output + day.Cached == 0 && snapshot.TotalTokens > 0)
        {
            return (snapshot.MonthInput, snapshot.MonthOutput, snapshot.MonthCached, snapshot.MonthRequests, true, "本月");
        }

        return (day.Input, day.Output, day.Cached, day.Requests, true, day.Day);
    }

    /// <summary>刷新所有已配置平台（内部节流：至少间隔 3 分钟；force 可跳过节流）。</summary>
    public void RefreshAll(bool force = false)
    {
        var now = DateTime.UtcNow;
        foreach (var platform in Platforms)
        {
            lock (_lock)
            {
                if (!force && _lastFetch.TryGetValue(platform.Id, out var last) && now - last < MinFetchInterval)
                {
                    continue;
                }
            }

            if (string.IsNullOrWhiteSpace(GetToken(platform.Id)))
            {
                continue;
            }

            _ = Task.Run(async () => await FetchOnceAsync(platform.Id));
        }
    }

    /// <summary>立即拉取单个平台并更新缓存，返回结果快照（供诊断页测试连接用）。</summary>
    public async Task<DirectPlatformSnapshot?> FetchOnceAsync(string platformId)
    {
        var token = GetToken(platformId);
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var snapshot = await DeepSeekPlatformClient.FetchAsync(token, CancellationToken.None);
        if (snapshot is null)
        {
            return null;
        }

        lock (_lock)
        {
            _snapshots[platformId] = snapshot;
            _lastFetch[platformId] = DateTime.UtcNow;
        }

        DataChanged?.Invoke();
        return snapshot;
    }

    private string? GetToken(string platformId)
    {
        if (platformId != "deepseek-platform")
        {
            return null;
        }

        var token = _settings.DeepSeekPlatformToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        // 从控制台复制时常带引号或空白，这里统一清理
        return token.Trim().Trim('"', '\'', ' ');
    }
}
