// TokenViewerExtension contributors. Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace TokenViewerExtension;

/// <summary>DeepSeek 用量详情页：官方今日/本月用量、余额、官方图表、控制台入口。</summary>
#pragma warning disable CA1001 // 页面实例缓存于扩展生命周期，无需手动释放
internal sealed partial class ProviderDetailPage : ListPage
{
    private readonly AppServices _app;
    private readonly System.Timers.Timer _timer;

    public ProviderDetailPage(AppServices app)
    {
        _app = app;
        Icon = new IconInfo("🐳");
        Title = "DeepSeek 用量";
        Name = "查看";

        if (!string.IsNullOrWhiteSpace(app.DeepSeek.ApiKey))
        {
            // 余额走共享缓存，避免多页面重复请求
            _app.BalanceCache.Get(app.DeepSeek);
        }

        _timer = new System.Timers.Timer(30_000) { AutoReset = true };
        _timer.Elapsed += (_, _) => RaiseItemsChanged();
        _timer.Start();
    }

    public override IListItem[] GetItems()
    {
        var items = new List<IListItem>();

        // 官方平台数据
        var (officialInput, officialOutput, officialCached, officialRequests, hasOfficial, dayLabel) =
            _app.Direct.GetTodayForProvider("deepseek");
        if (hasOfficial)
        {
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = "今日用量（官方）",
                Subtitle = $"输入 {Formatting.Tokens(officialInput)} · 输出 {Formatting.Tokens(officialOutput)}"
                    + (officialCached > 0 ? $" · 缓存 {Formatting.Tokens(officialCached)}" : string.Empty)
                    + $" · {officialRequests} 次请求 · 数据日期 {Formatting.Day(dayLabel!)}",
            });

            var snapshot = _app.Direct.GetSnapshot("deepseek-platform");
            if (snapshot is not null && snapshot.Error is null && snapshot.Days.Count > 0)
            {
                items.Add(new ListItem(new NoOpCommand())
                {
                    Title = "本月用量（官方）",
                    Subtitle = $"{Formatting.Tokens(snapshot.TotalTokens)} tokens · {snapshot.MonthRequests} 次请求"
                        + (snapshot.TotalCost is > 0 ? $" · 消费 {Formatting.Cost(snapshot.TotalCost.Value)}" : string.Empty)
                        + $" · 更新于 {snapshot.FetchedAt:HH:mm}",
                });

                items.Add(new ListItem(new UsageChartPage(DirectUsageService.GetPlatformInfo("deepseek-platform"), _app.Direct))
                {
                    Title = "查看官方用量图表",
                    Subtitle = "每日柱状图与模型占比",
                    Icon = new IconInfo("📈"),
                });
            }
        }

        // 余额
        var balance = _app.BalanceCache.Get(_app.DeepSeek);
        if (balance is not null)
        {
            items.Add(Header($"DeepSeek 余额：{balance.Text}"));
        }
        else if (_app.BalanceCache.IsPending("deepseek"))
        {
            items.Add(Header("DeepSeek 余额：查询中…"));
        }
        else
        {
            items.Add(Header("DeepSeek 余额：未查询"));
        }

        if (!string.IsNullOrWhiteSpace(_app.DeepSeek.ConsoleUrl))
        {
            items.Add(new ListItem(new OpenUrlCommand(_app.DeepSeek.ConsoleUrl))
            {
                Title = "打开官方用量控制台",
                Icon = new IconInfo("\uE774"), // Globe
            });
        }

        items.Add(new ListItem(new RefreshCommand(() =>
        {
            _app.Direct.RefreshAll(force: true);
            _app.BalanceCache.Refresh(_app.DeepSeek);
            RaiseItemsChanged();
        }))
        {
            Title = "立即刷新",
            Icon = new IconInfo("\uE72C"),
        });

        items.Add(new ListItem(new NoOpCommand())
        {
            Title = "提示",
            Subtitle = hasOfficial
                ? "官方数据来自 DeepSeek 平台（约 5 分钟延迟）；按天明细可能滞后，此时显示月度汇总"
                : "配置官方平台 Token 后显示用量（图表页有获取步骤）",
        });

        return [.. items];
    }

    private static ListItem Header(string text)
    {
        return new ListItem(new NoOpCommand()) { Title = text };
    }
}
