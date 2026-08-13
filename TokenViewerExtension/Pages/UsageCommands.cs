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
