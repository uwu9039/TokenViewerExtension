// TokenViewerExtension contributors. Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace TokenViewerExtension;

/// <summary>
/// 用量诊断页：检查各数据源的配置状态与连接情况，
/// 一键定位「为什么 token 消耗显示为 0」。
/// </summary>
#pragma warning disable CA1001 // 页面实例缓存于扩展生命周期，无需手动释放
internal sealed partial class DiagnosticsPage : ListPage
{
    private readonly AppServices _app;
    private readonly Dictionary<string, string> _proxyTests = [];
    private readonly Dictionary<string, string> _platformTests = [];
    private readonly System.Timers.Timer _timer;

    public DiagnosticsPage(AppServices app)
    {
        _app = app;
        Icon = new IconInfo("🔍");
        Title = "用量诊断";
        Name = "诊断";

        _timer = new System.Timers.Timer(30_000) { AutoReset = true };
        _timer.Elapsed += (_, _) => RaiseItemsChanged();
        _timer.Start();
    }

    public override IListItem[] GetItems()
    {
        var items = new List<IListItem>();

        items.Add(new ListItem(new NoOpCommand())
        {
            Title = _app.Proxy.IsRunning ? "本地代理：运行中" : "本地代理：未运行",
            Subtitle = _app.Proxy.IsRunning
                ? $"端口：{string.Join("、", _app.Registry.Providers.Where(p => p.Enabled).Select(p => $"{p.DisplayName} {p.Port}"))} · 客户端指向 http://127.0.0.1:{_app.Registry.Providers.FirstOrDefault(p => p.Enabled)?.Port ?? 0}/v1 即可被统计"
                : _app.Proxy.Error ?? "请检查设置",
        });

        var (_, _, _, requests, _) = _app.Recorder.GetTodayTotals(null);
        items.Add(new ListItem(new NoOpCommand())
        {
            Title = "今日代理请求数",
            Subtitle = $"{requests} 次（代理统计口径；为 0 说明没有客户端经过本地代理）",
        });

        items.Add(Header("数据源配置"));
        items.Add(ConfigRow("DeepSeek API Key", HasKey("deepseek"), "余额查询与代理转发用"));
        items.Add(ConfigRow("DeepSeek 官方平台 Token", _app.Direct.IsConfigured("deepseek-platform"), "官方 token 用量显示的前提，获取步骤见图表页"));

        items.Add(Header("代理链路测试"));
        foreach (var provider in _app.Registry.Providers.Where(p => p.Enabled && !string.IsNullOrWhiteSpace(p.ApiKey)))
        {
            _proxyTests.TryGetValue(provider.Id, out var result);
            items.Add(new ListItem(new AnonymousCommand(() => RunProxyTest(provider)) { Result = CommandResult.KeepOpen() })
            {
                Title = $"测试转发 {provider.DisplayName}（端口 {provider.Port}）",
                Subtitle = result ?? "点击执行：通过代理请求 /v1/models 验证 Key 与转发链路",
                Icon = new IconInfo("\uE945"), // LightningBolt
            });
        }

        if (_app.Direct.Platforms.Count > 0)
        {
            items.Add(Header("官方接口测试"));
            foreach (var platform in _app.Direct.Platforms)
            {
                _platformTests.TryGetValue(platform.Id, out var result);
                items.Add(new ListItem(new AnonymousCommand(() => RunPlatformTest(platform)) { Result = CommandResult.KeepOpen() })
                {
                    Title = $"测试 {platform.DisplayName} 官方接口",
                    Subtitle = result ?? "点击执行：拉取一次官方数据并报告结果",
                    Icon = new IconInfo("\uE945"),
                });

                // 0 tokens 但接口成功：极可能是字段名变化，提示查看原始响应
                var current = _app.Direct.GetSnapshot(platform.Id);
                if (current is not null && current.Error is null && current.Days.Count > 0 && current.TotalTokens == 0)
                {
                    items.Add(new ListItem(new NoOpCommand())
                    {
                        Title = "⚠️ 接口成功但 token 全为 0",
                        Subtitle = "若你确认正在消耗 token：请点下方「查看原始响应」并反馈，字段名可能已变化；也可能官方统计有延迟",
                        Icon = new IconInfo("\uEA39"),
                    });
                }

                if (current?.RawResponse is not null)
                {
                    items.Add(new ListItem(new RawResponsePage(platform.DisplayName, current.RawResponse))
                    {
                        Title = $"查看 {platform.DisplayName} 原始响应",
                        Subtitle = "接口返回的原文（排查字段变更用）",
                        Icon = new IconInfo("🧾"),
                    });
                    items.Add(new ListItem(new CopyTextCommand(current.RawResponse))
                    {
                        Title = "复制原始响应",
                        Subtitle = "一键复制到剪贴板，直接粘贴发给开发者",
                        Icon = new IconInfo("\uE8C8"), // Copy
                    });
                }
            }
        }

        items.Add(new ListItem(new NoOpCommand())
        {
            Title = "数据与配置位置",
            Subtitle = "%LOCALAPPDATA%\\TokenViewerExtension\\（settings.json 保存设置 · usage.json 保存代理统计）",
        });

        items.Add(new ListItem(new NoOpCommand())
        {
            Title = "为什么全是 0？",
            Subtitle = "token 消耗只有两个来源：① 客户端请求走本地代理（改 Base URL 为 http://127.0.0.1:{端口}/v1）；② 官方平台数据（配置平台 Token）。任选其一即可显示",
        });

        items.Add(new ListItem(new RefreshCommand(() =>
        {
            foreach (var provider in _app.Registry.Providers.Where(p => p.Enabled && !string.IsNullOrWhiteSpace(p.ApiKey)))
            {
                RunProxyTest(provider);
            }

            foreach (var platform in _app.Direct.Platforms)
            {
                RunPlatformTest(platform);
            }

            RaiseItemsChanged();
        }))
        {
            Title = "重新测试全部",
            Icon = new IconInfo("\uE72C"),
        });

        return [.. items];
    }

    private bool HasKey(string providerId)
    {
        return _app.Registry.Providers.Any(p => p.Id == providerId && !string.IsNullOrWhiteSpace(p.ApiKey));
    }

    private static ListItem ConfigRow(string name, bool configured, string hint)
    {
        return new ListItem(new NoOpCommand())
        {
            Title = configured ? $"✅ {name}：已配置" : $"❌ {name}：未配置",
            Subtitle = hint,
        };
    }

    private void RunProxyTest(ProviderConfig provider)
    {
        _ = Task.Run(async () =>
        {
            _proxyTests[provider.Id] = "测试中…";
            RaiseItemsChanged();
            _proxyTests[provider.Id] = await TestProxyAsync(provider);
            RaiseItemsChanged();
        });
    }

    private static async Task<string> TestProxyAsync(ProviderConfig provider)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var response = await client.GetAsync($"http://127.0.0.1:{provider.Port}/v1/models");
            return response.IsSuccessStatusCode
                ? $"✅ 转发正常（HTTP {(int)response.StatusCode}，API Key 有效）"
                : $"⚠️ 上游返回 HTTP {(int)response.StatusCode}（API Key 无效或接口异常）";
        }
        catch (Exception e)
        {
            return $"❌ 无法连接本地代理：{e.Message}";
        }
    }

    private void RunPlatformTest(DirectPlatformInfo platform)
    {
        _ = Task.Run(async () =>
        {
            _platformTests[platform.Id] = "测试中…";
            RaiseItemsChanged();
            var snapshot = await _app.Direct.FetchOnceAsync(platform.Id);
            _platformTests[platform.Id] = snapshot is null
                ? "未配置 Token"
                : snapshot.Error is not null
                    ? $"❌ {snapshot.Error}{(snapshot.RawResponse is not null ? $" | 原始响应：{snapshot.RawResponse}" : string.Empty)}"
                    : $"✅ 拉取成功：{snapshot.Days.Count} 天数据 · 共 {Formatting.Tokens(snapshot.TotalTokens)} tokens";
            RaiseItemsChanged();
        });
    }

    private static ListItem Header(string text)
    {
        return new ListItem(new NoOpCommand()) { Title = text };
    }
}
