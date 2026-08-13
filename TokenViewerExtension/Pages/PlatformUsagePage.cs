// TokenViewerExtension contributors. Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace TokenViewerExtension;

/// <summary>
/// 官方平台数据页：显示与官方监控平台一致的数据（DeepSeek 官方平台 / OpenAI / Anthropic），
/// 按天列表 + 当月按模型分解 + 余额 + 消费金额。
/// </summary>
#pragma warning disable CA1001 // 页面实例缓存于扩展生命周期，无需手动释放
internal sealed partial class PlatformUsagePage : ListPage
{
    private readonly DirectPlatformInfo _platform;
    private readonly DirectUsageService _service;
    private readonly System.Timers.Timer _timer;

    public PlatformUsagePage(DirectPlatformInfo platform, DirectUsageService service)
    {
        _platform = platform;
        _service = service;
        Icon = new IconInfo(platform.Icon);
        Title = $"{platform.DisplayName} 官方用量";
        Name = "查看";

        _timer = new System.Timers.Timer(30_000) { AutoReset = true };
        _timer.Elapsed += (_, _) => RaiseItemsChanged();
        _timer.Start();
    }

    public override IListItem[] GetItems()
    {
        var items = new List<IListItem>();
        var snapshot = _service.GetSnapshot(_platform.Id);

        if (snapshot?.Error is not null)
        {
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = "拉取失败",
                Subtitle = snapshot.Error,
                Icon = new IconInfo("\uEA39"), // Warning
            });
        }
        else
        {
            // 接口成功但 0 tokens：提示查看原始响应
            if (snapshot is not null && snapshot.Models.Count == 0 && snapshot.TotalTokens == 0)
            {
                items.Add(new ListItem(new NoOpCommand())
                {
                    Title = "⚠️ 接口成功但 token 全为 0",
                    Subtitle = "若你确认正在消耗 token：点下方「查看原始响应」并反馈，字段名可能已变化；也可能官方统计有延迟（余额实时变化，用量统计可能有延迟）",
                    Icon = new IconInfo("\uEA39"),
                });
            }

            var fetched = snapshot is not null ? $"（更新于 {snapshot.FetchedAt:HH:mm:ss}）" : "（点击立即刷新）";
            var costText = snapshot?.TotalCost is > 0 ? $" · 消费 {Formatting.Cost(snapshot.TotalCost.Value)}" : string.Empty;
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = "本月用量",
                Subtitle = $"输入 {Formatting.Tokens(snapshot?.MonthInput ?? 0)} · 输出 {Formatting.Tokens(snapshot?.MonthOutput ?? 0)}" +
                    (snapshot is not null && snapshot.MonthCached > 0 ? $" · 缓存 {Formatting.Tokens(snapshot.MonthCached)}" : string.Empty) +
                    $" · {snapshot?.MonthRequests ?? 0} 次请求{costText} {fetched}",
            });

            if (snapshot is not null && snapshot.BalanceText is not null)
            {
                items.Add(Header($"平台余额：{snapshot.BalanceText}"));
            }

            if (snapshot is not null && snapshot.Models.Count > 0)
            {
                items.Add(Header("按模型（本月）"));
                foreach (var model in snapshot.Models)
                {
                    items.Add(new ListItem(new NoOpCommand())
                    {
                        Title = model.Model,
                        Subtitle = $"输入 {Formatting.Tokens(model.Input)} · 输出 {Formatting.Tokens(model.Output)}"
                            + (model.Cached > 0 ? $" · 缓存 {Formatting.Tokens(model.Cached)}" : string.Empty)
                            + $" · {model.Requests} 次",
                    });
                }
            }

            if (snapshot is not null && snapshot.Days.Count > 0 && snapshot.DaysHaveData)
            {
                items.Add(Header("按天（近 31 天）"));
                foreach (var day in snapshot.Days)
                {
                    items.Add(new ListItem(new NoOpCommand())
                    {
                        Title = Formatting.Day(day.Day),
                        Subtitle = $"输入 {Formatting.Tokens(day.Input)} · 输出 {Formatting.Tokens(day.Output)}"
                            + (day.Cached > 0 ? $" · 缓存 {Formatting.Tokens(day.Cached)}" : string.Empty)
                            + $" · {day.Requests} 次"
                            + (day.Cost is > 0 ? $" · {Formatting.Cost(day.Cost.Value)}" : string.Empty),
                    });
                }
            }
            else if (snapshot is not null && snapshot.Days.Count > 0 && snapshot.Models.Count > 0)
            {
                items.Add(Header("按天（近 31 天）"));
                items.Add(new ListItem(new NoOpCommand())
                {
                    Title = "官方按天数据尚未更新（统计延迟）",
                    Subtitle = "当前展示的是本月汇总（上方）；按天明细待官方更新后自动出现",
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(_platform.ConsoleUrl))
        {
            items.Add(new ListItem(new OpenUrlCommand(_platform.ConsoleUrl))
            {
                Title = "打开官方监控平台",
                Icon = new IconInfo("\uE774"), // Globe
            });
        }

        items.Add(new ListItem(new UsageChartPage(_platform, _service))
        {
            Title = "查看用量图表",
            Subtitle = "每日柱状图与模型占比",
            Icon = new IconInfo("📈"),
        });

        if (snapshot?.RawResponse is not null)
        {
            items.Add(new ListItem(new RawResponsePage(_platform.DisplayName, snapshot.RawResponse))
            {
                Title = "查看原始响应",
                Subtitle = "接口返回的原文（排查字段变更用）",
                Icon = new IconInfo("🧾"),
            });
            items.Add(new ListItem(new CopyTextCommand(snapshot.RawResponse))
            {
                Title = "复制原始响应",
                Subtitle = "一键复制到剪贴板，直接粘贴发给开发者",
                Icon = new IconInfo("\uE8C8"), // Copy
            });
        }

        items.Add(new ListItem(new RefreshCommand(() =>
        {
            _service.RefreshAll(force: true);
            RaiseItemsChanged();
        }))
        {
            Title = "立即刷新",
            Subtitle = "重新从官方接口拉取数据",
            Icon = new IconInfo("\uE72C"),
        });

        items.Add(new ListItem(new NoOpCommand())
        {
            Title = "提示",
            Subtitle = _platform.Id == "deepseek-platform"
                ? "数据来自 DeepSeek 平台内部接口，约 5 分钟延迟；Token 过期后需在设置中更新"
                : "数据来自官方用量 API，与官方监控页一致",
        });

        return [.. items];
    }

    private static ListItem Header(string text)
    {
        return new ListItem(new NoOpCommand()) { Title = text };
    }
}
