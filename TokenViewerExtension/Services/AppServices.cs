// TokenViewerExtension contributors. Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

namespace TokenViewerExtension;

/// <summary>扩展内共享的服务容器，负责装配各组件并缓存页面实例。</summary>
internal sealed class AppServices
{
    private ProviderDetailPage? _detailPage;
    private readonly Dictionary<string, PlatformUsagePage> _platformPages = [];

    public SettingsManager Settings { get; }

    public DirectUsageService Direct { get; }

    public BalanceCache BalanceCache { get; }

    public BalancesPage Balances { get; }

    public DiagnosticsPage Diagnostics { get; }

    /// <summary>当前 DeepSeek 配置。</summary>
    public ProviderConfig DeepSeek => Settings.DeepSeek;

    public AppServices(SettingsManager settings, DirectUsageService direct)
    {
        Settings = settings;
        Direct = direct;
        BalanceCache = new BalanceCache();
        Balances = new BalancesPage(this);
        Diagnostics = new DiagnosticsPage(this);
    }

    /// <summary>获取（或创建并缓存）DeepSeek 用量详情页。</summary>
    public ProviderDetailPage GetDetailPage()
    {
        return _detailPage ??= new ProviderDetailPage(this);
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
