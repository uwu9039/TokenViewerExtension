// TokenViewerExtension contributors. Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace TokenViewerExtension;

/// <summary>账户监控页：官方余额 + token 用量与请求数（官方数据），含官方用量图表入口，定时自动刷新。</summary>
#pragma warning disable CA1001 // 页面实例缓存于扩展生命周期，无需手动释放
internal sealed partial class BalancesPage : ListPage
{
    private readonly AppServices _app;
    private readonly System.Timers.Timer _timer;

    public BalancesPage(AppServices app)
    {
        _app = app;
        Icon = new IconInfo("💳");
        Title = "账户监控";
        Name = "查看";

        _timer = new System.Timers.Timer(60_000) { AutoReset = true };
        _timer.Elapsed += (_, _) =>
        {
            Refresh(force: false);
            RaiseItemsChanged();
        };
        _timer.Start();
    }

    public override IListItem[] GetItems()
    {
        Refresh(force: false);

        var items = new List<IListItem>();
        var provider = _app.DeepSeek;
        if (string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = "暂无可查询的账户",
                Subtitle = "在扩展设置中填入 DeepSeek API Key 后即可显示余额与用量",
            });
        }
        else
        {
            var balance = _app.BalanceCache.Get(provider);
            var balanceText = balance is not null
                ? balance.Text
                : _app.BalanceCache.IsPending(provider.Id)
                    ? "查询中…"
                    : "未查询";

            items.Add(new ListItem(new NoOpCommand())
            {
                Title = "DeepSeek 账户",
                Subtitle = $"余额 {balanceText} · {BuildUsageText()}",
                Icon = new IconInfo("🐳"),
            });

            items.Add(new ListItem(new UsageChartPage(DirectUsageService.GetPlatformInfo("deepseek-platform"), _app.Direct))
            {
                Title = "DeepSeek 用量图表（官方）",
                Subtitle = _app.Direct.IsConfigured("deepseek-platform")
                    ? "每日 token 消耗柱状图与模型占比"
                    : "需要配置平台 Token（页面内有获取步骤）",
                Icon = new IconInfo("📈"),
            });
        }

        items.Add(new ListItem(new RefreshCommand(() =>
        {
            Refresh(force: true);
            _app.Direct.RefreshAll(force: true);
            RaiseItemsChanged();
        }))
        {
            Title = "立即刷新",
            Subtitle = "重新查询余额与官方用量",
            Icon = new IconInfo("\uE72C"),
        });

        items.Add(new ListItem(new NoOpCommand())
        {
            Title = "提示",
            Subtitle = "余额与官方用量直连查询，每 60 秒自动刷新",
        });

        return [.. items];
    }

    /// <summary>用量描述（官方平台数据）。</summary>
    private string BuildUsageText()
    {
        var (input, output, cached, requests, isOfficial, dayLabel) = _app.Direct.GetTodayForProvider("deepseek");
        if (isOfficial)
        {
            return $"官方 {Formatting.Day(dayLabel!)}：输入 {Formatting.Tokens(input)} · 输出 {Formatting.Tokens(output)}"
                + (cached > 0 ? $" · 缓存 {Formatting.Tokens(cached)}" : string.Empty)
                + $" · {requests} 次请求";
        }

        return _app.Direct.IsConfigured("deepseek-platform")
            ? "官方数据尚未拉取，点「立即刷新」"
            : "配置平台 Token 后显示官方用量（图表页有获取步骤）";
    }

    private void Refresh(bool force)
    {
        if (string.IsNullOrWhiteSpace(_app.DeepSeek.ApiKey))
        {
            return;
        }

        if (force)
        {
            _app.BalanceCache.Refresh(_app.DeepSeek);
        }
        else
        {
            _app.BalanceCache.Get(_app.DeepSeek);
        }
    }
}
