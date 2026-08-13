// TokenViewerExtension contributors. Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

namespace TokenViewerExtension;

/// <summary>扩展内共享的服务容器，负责装配各组件并缓存页面实例。</summary>
internal sealed class AppServices
{
    private readonly Dictionary<string, ProviderDetailPage> _detailPages = [];
    private readonly Dictionary<string, PlatformUsagePage> _platformPages = [];

    public SettingsManager Settings { get; }

    public TokenUsageRecorder Recorder { get; }

    public LocalProxyServer Proxy { get; }

    public ProviderRegistry Registry { get; }

    public DirectUsageService Direct { get; }

    public BalanceCache BalanceCache { get; }

    public BalancesPage Balances { get; }

    public DiagnosticsPage Diagnostics { get; }

    public AppServices(SettingsManager settings, TokenUsageRecorder recorder, LocalProxyServer proxy, ProviderRegistry registry, DirectUsageService direct)
    {
        Settings = settings;
        Recorder = recorder;
        Proxy = proxy;
        Registry = registry;
        Direct = direct;
        BalanceCache = new BalanceCache();
        Balances = new BalancesPage(this);
        Diagnostics = new DiagnosticsPage(this);
    }

    /// <summary>获取（或创建并缓存）某提供商的明细页，避免每次刷新都新建导致重复查询余额。</summary>
    public ProviderDetailPage GetDetailPage(ProviderConfig provider)
    {
        if (!_detailPages.TryGetValue(provider.Id, out var page))
        {
            page = new ProviderDetailPage(this, provider);
            _detailPages[provider.Id] = page;
        }

        return page;
    }

    /// <summary>获取（或创建并缓存）某直连平台的官方数据页。</summary>
    public PlatformUsagePage GetPlatformPage(DirectPlatformInfo platform)
    {
        if (!_platformPages.TryGetValue(platform.Id, out var page))
        {
            page = new PlatformUsagePage(platform, Direct);
            _platformPages[platform.Id] = page;
        }

        return page;
    }
}
