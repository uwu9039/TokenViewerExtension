// TokenViewerExtension contributors. Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace TokenViewerExtension;

/// <summary>
/// 总览页：今日总计（官方数据）、用量详情、官方用量、账户监控、用量诊断入口。
/// </summary>
#pragma warning disable CA1001 // 页面实例缓存于扩展生命周期，无需手动释放
internal sealed partial class OverviewPage : ListPage
{
    private readonly AppServices _app;
    private readonly System.Timers.Timer _timer;

    public OverviewPage(AppServices app)
    {
        Icon = new IconInfo("📊");
        Title = "Token 用量监控";
        Name = "打开";
        _app = app;

        _timer = new System.Timers.Timer(app.Settings.RefreshIntervalSeconds * 1000) { AutoReset = true };
        _timer.Elapsed += (_, _) => RaiseItemsChanged();
        _timer.Start();
    }

    /// <summary>供外部（如设置变更时）触发页面刷新。RaiseItemsChanged 是受保护成员。</summary>
    public void Refresh() => RaiseItemsChanged();

    public override IListItem[] GetItems()
    {
        var items = new List<IListItem>();

        var (input, output, cached, requests, isOfficial, dayLabel) = _app.Direct.GetTodayForProvider("deepseek");
        items.Add(new ListItem(new NoOpCommand())
        {
            Title = "今日总计",
            Subtitle = isOfficial
                ? $"输入 {Formatting.Tokens(input)} · 输出 {Formatting.Tokens(output)}"
                    + (cached > 0 ? $" · 缓存 {Formatting.Tokens(cached)}" : string.Empty)
                    + $" · {requests} 次请求（官方 {Formatting.Day(dayLabel!)}）"
                : "暂无数据 · 配置官方平台 Token 后显示",
        });

        if (!isOfficial)
        {
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = "没有数据？",
                Subtitle = "在扩展设置中配置「DeepSeek 官方平台 Token」后自动显示官方用量，获取步骤见「用量图表」页",
                Icon = new IconInfo("\uEA39"), // Warning
            });
        }

        // 用量详情
        string detailSubtitle;
        if (isOfficial)
        {
            detailSubtitle = $"官方 {Formatting.Day(dayLabel!)}：输入 {Formatting.Tokens(input)} · 输出 {Formatting.Tokens(output)} · {requests} 次请求";
        }
        else
        {
            var balance = _app.BalanceCache.Get(_app.DeepSeek);
            detailSubtitle = balance is not null
                ? $"余额 {balance.Text} · 配置平台 Token 后显示官方用量"
                : "配置平台 Token 后显示官方用量";
        }

        items.Add(new ListItem(_app.GetDetailPage())
        {
            Title = "DeepSeek 用量详情",
            Subtitle = detailSubtitle,
            Icon = new IconInfo("🐳"),
        });

        // DeepSeek 官方用量（官方平台数据页）
        var platform = DirectUsageService.GetPlatformInfo("deepseek-platform");
        var snapshot = _app.Direct.GetSnapshot(platform.Id);
        var platformSubtitle = snapshot?.Error is not null
            ? snapshot.Error
            : snapshot is null
                ? _app.Direct.IsConfigured(platform.Id) ? "尚未拉取，点击进入后自动获取" : "未配置平台 Token（见图表页获取步骤）"
                : $"本月 {Formatting.Tokens(snapshot.TotalTokens)} tokens"
                    + (snapshot.TotalCost is > 0 ? $" · {Formatting.Cost(snapshot.TotalCost.Value)}" : string.Empty)
                    + $" · 更新于 {snapshot.FetchedAt:HH:mm}";
        items.Add(new ListItem(_app.GetPlatformPage(platform))
        {
            Title = "DeepSeek 官方用量",
            Subtitle = platformSubtitle,
            Icon = new IconInfo(platform.Icon),
        });

        items.Add(new ListItem(_app.Balances)
        {
            Title = "账户监控",
            Subtitle = "余额 + 今日用量 + 官方图表",
            Icon = new IconInfo("💳"),
        });

        items.Add(new ListItem(_app.Diagnostics)
        {
            Title = "用量诊断",
            Subtitle = "检查配置与连接，定位为什么显示为 0",
            Icon = new IconInfo("🔍"),
        });

        items.Add(new ListItem(new RefreshCommand(() => RaiseItemsChanged()))
        {
            Title = "立即刷新",
            Subtitle = "重新读取用量数据",
            Icon = new IconInfo("\uE72C"),
        });

        items.Add(new ListItem(new NoOpCommand())
        {
            Title = "使用说明",
            Subtitle = "在扩展设置中配置 API Key（余额）与官方平台 Token（用量），即可查看 DeepSeek 用量",
            Icon = new IconInfo("\uE946"), // Help
        });

        return [.. items];
    }
}
