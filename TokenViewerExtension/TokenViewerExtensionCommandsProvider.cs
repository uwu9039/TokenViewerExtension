// TokenViewerExtension contributors. Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace TokenViewerExtension;

public partial class TokenViewerExtensionCommandsProvider : CommandProvider
{
    private readonly AppServices _app;
    private readonly System.Timers.Timer _refreshTimer;
    private OverviewPage? _overview;
    private DateTime _lastItemsChanged = DateTime.MinValue;

    public TokenViewerExtensionCommandsProvider()
    {
        DisplayName = "Token 用量监控";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");

        var store = UsageStore.Load();
        var registry = new ProviderRegistry();
        var settings = new SettingsManager(registry);
        var recorder = new TokenUsageRecorder(store);
        var proxy = new LocalProxyServer(recorder, registry);
        var direct = new DirectUsageService(settings);
        _app = new AppServices(settings, recorder, proxy, registry, direct);

        // 设置页由命令面板宿主渲染并持久化
        Settings = settings.Settings;

        _app.Settings.Changed += OnSettingsChanged;
        _app.Recorder.UsageChanged += OnUsageChanged;
        _app.Proxy.StateChanged += () => RaiseItemsChanged();
        _app.Direct.DataChanged += () => RaiseItemsChanged();

        _refreshTimer = new System.Timers.Timer(settings.RefreshIntervalSeconds * 1000) { AutoReset = true };
        _refreshTimer.Elapsed += (_, _) => RaiseItemsChanged();
        _refreshTimer.Start();

        // 扩展被命令面板加载后自动启动本地代理
        _app.Proxy.Restart();
    }

    public override ICommandItem[] TopLevelCommands()
    {
        _overview ??= new OverviewPage(_app);

        // 今日总计（官方优先）
        var (totalInput, totalOutput, _, totalRequests, _) = _app.Recorder.GetTodayTotals(null);
        var (officialInput, officialOutput, _, officialRequests, hasOfficial, _) = _app.Direct.GetTodayForProvider("deepseek");
        if (hasOfficial)
        {
            var (proxyInput, proxyOutput, _, proxyRequests, _) = _app.Recorder.GetTodayTotals("deepseek");
            totalInput = totalInput - proxyInput + officialInput;
            totalOutput = totalOutput - proxyOutput + officialOutput;
            totalRequests = totalRequests - proxyRequests + officialRequests;
        }

        var commands = new List<ICommandItem>
        {
            new CommandItem(_overview)
            {
                Title = "Token 用量监控",
                Subtitle = $"今日 {Formatting.Tokens(totalInput + totalOutput)} tokens · {totalRequests} 次请求{(hasOfficial ? "（官方）" : string.Empty)}",
                Icon = new IconInfo("📊"),
            },
            new CommandItem(_app.Balances)
            {
                Title = "账户监控",
                Subtitle = "DeepSeek 余额 + 今日用量 + 官方图表（无需代理）",
                Icon = new IconInfo("💳"),
            },
            new CommandItem(_app.Diagnostics)
            {
                Title = "用量诊断",
                Subtitle = "检查配置与连接，定位为什么没有数据",
                Icon = new IconInfo("🔍"),
            },
        };

        return [.. commands];
    }

    private void OnSettingsChanged()
    {
        _refreshTimer.Interval = _app.Settings.RefreshIntervalSeconds * 1000;
        _app.Proxy.Restart();
        _app.Direct.RefreshAll(force: true);
        _overview?.Refresh();
        RaiseItemsChanged();
    }

    private void OnUsageChanged()
    {
        // 节流：代理请求频繁时最多每 5 秒刷新一次顶层命令
        if (DateTime.UtcNow - _lastItemsChanged < TimeSpan.FromSeconds(5))
        {
            return;
        }

        _lastItemsChanged = DateTime.UtcNow;
        RaiseItemsChanged();
    }
}
