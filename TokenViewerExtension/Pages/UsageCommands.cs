// TokenViewerExtension contributors. Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace TokenViewerExtension;

/// <summary>立即刷新当前页面/数据。</summary>
internal sealed partial class RefreshCommand : InvokableCommand
{
    private readonly Action _onRefresh;

    public override string Name => "立即刷新";

    public override IconInfo Icon => new("\uE72C"); // Refresh

    public RefreshCommand(Action onRefresh)
    {
        _onRefresh = onRefresh;
    }

    public override CommandResult Invoke()
    {
        _onRefresh();
        return CommandResult.KeepOpen();
    }
}

/// <summary>重启本地代理（设置变更后可用）。</summary>
internal sealed partial class RestartProxyCommand : InvokableCommand
{
    private readonly LocalProxyServer _proxy;

    public override string Name => "重启本地代理";

    public override IconInfo Icon => new("\uE777"); // Refresh key

    public RestartProxyCommand(LocalProxyServer proxy)
    {
        _proxy = proxy;
    }

    public override CommandResult Invoke()
    {
        _proxy.Restart();
        return CommandResult.ShowToast(_proxy.IsRunning ? "本地代理已重启" : $"代理启动失败：{_proxy.Error}");
    }
}
