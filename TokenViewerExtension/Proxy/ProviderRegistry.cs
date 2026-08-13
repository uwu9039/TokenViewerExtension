// TokenViewerExtension contributors. Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace TokenViewerExtension;

/// <summary>一个已配置的提供商（来自设置页）。</summary>
internal sealed record ProviderConfig(
    string Id,
    string DisplayName,
    string BaseUrl,
    string ApiKey,
    int Port,
    bool Enabled,
    string ConsoleUrl,
    string IconEmoji)
{
    public bool IsDeepSeek => BaseUrl.Contains("deepseek.com", StringComparison.OrdinalIgnoreCase);
}

/// <summary>提供商配置的运行时快照，供代理与页面读取。</summary>
internal sealed class ProviderRegistry
{
    private volatile List<ProviderConfig> _providers = [];

    public IReadOnlyList<ProviderConfig> Providers => _providers;

    public void Update(IEnumerable<ProviderConfig> providers)
    {
        _providers = providers.ToList();
    }

    public ProviderConfig? FindByPort(int port)
    {
        return _providers.FirstOrDefault(p => p.Enabled && p.Port == port);
    }
}
