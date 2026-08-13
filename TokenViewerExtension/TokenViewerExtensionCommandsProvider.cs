// TokenViewerExtension contributors. Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace TokenViewerExtension;

public partial class TokenViewerExtensionCommandsProvider : CommandProvider
{
    private readonly AppServices _app;
    private readonly System.Timers.Timer _refreshTimer;
    private OverviewPage? _overview;

    public TokenViewerExtensionCommandsProvider()
    {
        DisplayName = "Token 用量监控";
        Icon = new IconInfo("📊");

        var settings = new SettingsManager();
        var direct = new DirectUsageService(settings);
        _app = new AppServices(settings, direct);

        // 设置页由命令面板宿主渲染并持久化
        Settings = settings.Settings;

        _app.Settings.Changed += OnSettingsChanged;
        _app.Direct.DataChanged += () => RaiseItemsChanged();

        _refreshTimer = new System.Timers.Timer(settings.RefreshIntervalSeconds * 1000) { AutoReset = true };
        _refreshTimer.Elapsed += (_, _) => RaiseItemsChanged();
        _refreshTimer.Start();
    }

    public override ICommandItem[] TopLevelCommands()
    {
        _overview ??= new OverviewPage(_app);

        var (input, output, _, requests, isOfficial, dayLabel) = _app.Direct.GetTodayForProvider("deepseek");
        var subtitle = isOfficial
            ? $"官方 {Formatting.Day(dayLabel!)}：输入 {Formatting.Tokens(input)} · 输出 {Formatting.Tokens(output)} · {requests} 次请求"
            : "配置官方平台 Token 后显示用量";

        return
        [
            new CommandItem(_overview)
            {
                Title = "Token 用量监控",
                Subtitle = subtitle,
                Icon = new IconInfo("📊"),
            },
            new CommandItem(_app.Balances)
            {
                Title = "账户监控",
                Subtitle = "DeepSeek 余额 + 今日用量 + 官方图表",
                Icon = new IconInfo("💳"),
            },
            new CommandItem(_app.Diagnostics)
            {
                Title = "用量诊断",
                Subtitle = "检查配置与连接，定位为什么没有数据",
                Icon = new IconInfo("🔍"),
            },
        ];
    }

    private void OnSettingsChanged()
    {
        _refreshTimer.Interval = _app.Settings.RefreshIntervalSeconds * 1000;
        _app.Direct.RefreshAll(force: true);
        _overview?.Refresh();
        RaiseItemsChanged();
    }
}
