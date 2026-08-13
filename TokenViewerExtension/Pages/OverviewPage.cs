// TokenViewerExtension contributors. Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace TokenViewerExtension;

/// <summary>
/// 总览页（已精简为仅 DeepSeek）：代理状态、今日总计（官方数据优先）、
/// 用量详情 / 官方用量 / 账户监控 / 用量诊断入口。
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
        var deepseek = _app.Registry.Providers.First(p => p.Id == "deepseek");

        items.Add(new ListItem(new NoOpCommand())
        {
            Title = _app.Proxy.IsRunning ? "本地代理运行中" : "本地代理未运行",
            Subtitle = _app.Proxy.IsRunning
                ? $"端口 {deepseek.Port} · 客户端 Base URL 指向 http://127.0.0.1:{deepseek.Port}/v1 可被实时统计（不适用于后台 agent）"
                : _app.Proxy.Error ?? "请检查扩展设置",
            Icon = new IconInfo(_app.Proxy.IsRunning ? "\uE73E" : "\uEA39"), // CheckMark / Warning
        });

        // 今日总计：DeepSeek 有官方数据时用官方数字替换代理统计
        var (totalInput, totalOutput, _, totalRequests, totalEstimates) = _app.Recorder.GetTodayTotals(null);
        var (officialInput, officialOutput, _, officialRequests, hasOfficial, _) = _app.Direct.GetTodayForProvider("deepseek");
        if (hasOfficial)
        {
            var (proxyInput, proxyOutput, _, proxyRequests, _) = _app.Recorder.GetTodayTotals("deepseek");
            totalInput = totalInput - proxyInput + officialInput;
            totalOutput = totalOutput - proxyOutput + officialOutput;
            totalRequests = totalRequests - proxyRequests + officialRequests;
        }

        items.Add(new ListItem(new NoOpCommand())
        {
            Title = "今日总计",
            Subtitle = $"输入 {Formatting.Tokens(totalInput)} · 输出 {Formatting.Tokens(totalOutput)} · {totalRequests} 次请求"
                + (hasOfficial ? "（官方数据）" : string.Empty)
                + (totalEstimates ? "（部分为估算）" : string.Empty),
        });

        if (totalRequests == 0 && !hasOfficial)
        {
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = "为什么全是 0？",
                Subtitle = "token 消耗只有两个来源：① 客户端请求走本地代理（不适合后台 agent）；② 官方平台数据（推荐：配置平台 Token 后自动显示，无需代理）。Token 获取步骤见「用量图表」页",
                Icon = new IconInfo("\uEA39"), // Warning
            });
        }

        // DeepSeek 用量详情（官方 + 代理 + 余额 + 图表）
        string detailSubtitle;
        if (string.IsNullOrWhiteSpace(deepseek.ApiKey))
        {
            detailSubtitle = "未配置 API Key，请打开扩展设置填写";
        }
        else if (hasOfficial)
        {
            detailSubtitle = $"官方：输入 {Formatting.Tokens(officialInput)} · 输出 {Formatting.Tokens(officialOutput)} · {officialRequests} 次请求";
        }
        else
        {
            var (pInput, pOutput, _, pRequests, _) = _app.Recorder.GetTodayTotals("deepseek");
            if (pRequests > 0)
            {
                detailSubtitle = $"今日 输入 {Formatting.Tokens(pInput)} · 输出 {Formatting.Tokens(pOutput)} · {pRequests} 次请求（代理）";
            }
            else
            {
                var balance = _app.BalanceCache.Get(deepseek);
                detailSubtitle = balance is not null
                    ? $"余额 {balance.Text} · 配置平台 Token 后显示官方用量"
                    : "今日暂无记录 · 配置平台 Token 可显示官方数据";
            }
        }

        items.Add(new ListItem(_app.GetDetailPage(deepseek))
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
            Subtitle = "余额 + 今日用量 + 官方图表（仅需 API Key，无需代理）",
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

        items.Add(new ListItem(new RestartProxyCommand(_app.Proxy))
        {
            Title = "重启本地代理",
            Subtitle = "修改端口或密钥设置后可手动重启",
            Icon = new IconInfo("\uE777"),
        });

        items.Add(new ListItem(new NoOpCommand())
        {
            Title = "使用说明",
            Subtitle = "① 官方路径（推荐）：设置中配置平台 Token，总览/详情自动并入官方数据；② 代理路径：客户端 Base URL 改为 http://127.0.0.1:8788/v1",
            Icon = new IconInfo("\uE946"), // Help
        });

        return [.. items];
    }
}
