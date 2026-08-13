// TokenViewerExtension contributors. Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace TokenViewerExtension;

/// <summary>
/// 提供商明细页：今日用量、按模型分解、近 7 天、余额、官方控制台入口。
/// DeepSeek 提供商在配置了官方平台 Token 时，并入官方平台数据（今日/本月/图表）。
/// </summary>
#pragma warning disable CA1001 // 页面实例缓存于扩展生命周期，无需手动释放
internal sealed partial class ProviderDetailPage : ListPage
{
    private readonly AppServices _app;
    private readonly ProviderConfig _provider;
    private readonly System.Timers.Timer _timer;

    public ProviderDetailPage(AppServices app, ProviderConfig provider)
    {
        _app = app;
        _provider = provider;
        Icon = new IconInfo(provider.IconEmoji);
        Title = $"{provider.DisplayName} 用量";
        Name = "查看";

        if (!string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            // 余额走共享缓存，避免多页面重复请求
            _app.BalanceCache.Get(provider);
        }

        _timer = new System.Timers.Timer(30_000) { AutoReset = true };
        _timer.Elapsed += (_, _) => RaiseItemsChanged();
        _timer.Start();
    }

    public override IListItem[] GetItems()
    {
        var items = new List<IListItem>();

        // ---- 官方平台数据（仅 DeepSeek，配置平台 Token 后可用）----
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

        // ---- 代理统计 ----
        var (input, output, cached, requests, hasEstimates) = _app.Recorder.GetTodayTotals(_provider.Id);
        var models = _app.Recorder.GetTodayModelUsage(_provider.Id);
        var cost = PricingTable.EstimateTotalCost(models);

        items.Add(new ListItem(new NoOpCommand())
        {
            Title = hasOfficial ? "今日用量（代理统计）" : "今日用量",
            Subtitle = $"输入 {Formatting.Tokens(input)} · 输出 {Formatting.Tokens(output)}"
                + (cached > 0 ? $" · 缓存 {Formatting.Tokens(cached)}" : string.Empty)
                + $" · {requests} 次请求 · 估算费用 {Formatting.Cost(cost)}{(hasEstimates ? "（部分估算）" : string.Empty)}",
        });

        if (models.Count > 0)
        {
            items.Add(Header("按模型（今日）"));
            foreach (var model in models)
            {
                var modelCost = PricingTable.EstimateCost(model.Model, model.Input, model.Output, model.Cached);
                items.Add(new ListItem(new NoOpCommand())
                {
                    Title = model.Model,
                    Subtitle = $"输入 {Formatting.Tokens(model.Input)} · 输出 {Formatting.Tokens(model.Output)}"
                        + (model.Cached > 0 ? $" · 缓存 {Formatting.Tokens(model.Cached)}" : string.Empty)
                        + $" · {model.Requests} 次"
                        + (modelCost > 0 ? $" · {Formatting.Cost(modelCost)}" : " · 未收录单价"),
                });
            }
        }

        var days = _app.Recorder.GetLastDays(_provider.Id, 7);
        if (days.Count > 0)
        {
            items.Add(Header("近 7 天（代理）"));
            foreach (var day in days)
            {
                items.Add(new ListItem(new NoOpCommand())
                {
                    Title = Formatting.Day(day.Day),
                    Subtitle = $"输入 {Formatting.Tokens(day.Input)} · 输出 {Formatting.Tokens(day.Output)} · {day.Requests} 次请求",
                });
            }
        }

        var balance = _app.BalanceCache.Get(_provider);
        if (balance is not null)
        {
            items.Add(Header($"DeepSeek 余额：{balance.Text}"));
        }
        else if (_app.BalanceCache.IsPending(_provider.Id))
        {
            items.Add(Header("DeepSeek 余额：查询中…"));
        }
        else
        {
            items.Add(Header("DeepSeek 余额：未查询"));
        }

        if (!string.IsNullOrWhiteSpace(_provider.ConsoleUrl))
        {
            items.Add(new ListItem(new OpenUrlCommand(_provider.ConsoleUrl))
            {
                Title = "打开官方用量控制台",
                Icon = new IconInfo("\uE774"), // Globe
            });
        }

        items.Add(new ListItem(new RefreshCommand(() =>
        {
            _app.Direct.RefreshAll(force: true);
            _app.BalanceCache.Refresh(_provider);

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
                ? "官方数据来自 DeepSeek 平台（约 5 分钟延迟）；代理统计为实时请求计数，流式无 usage 时按字符估算"
                : "流式响应未返回 usage 时按字符数估算；费用为估算值，仅供参考",
        });

        return [.. items];
    }

    private static ListItem Header(string text)
    {
        return new ListItem(new NoOpCommand()) { Title = text };
    }
}
