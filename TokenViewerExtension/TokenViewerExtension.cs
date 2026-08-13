// TokenViewerExtension — 扩展根对象
// Copyright (c) TokenViewerExtension contributors. Licensed under the MIT License.

using System;
using System.Runtime.InteropServices;
using Microsoft.CommandPalette.Extensions;

namespace TokenViewerExtension;

/// <summary>
/// 扩展根对象：向命令面板宿主暴露本扩展提供的组件。
/// 当前仅提供命令（Commands）提供器，负责注册全部命令与页面。
/// </summary>
[Guid("24e5fecd-2dc1-4f0a-9b02-350545898591")]
public sealed partial class TokenViewerExtension : IExtension, IDisposable
{
    private readonly Action _onDisposed;
    private readonly TokenViewerExtensionCommandsProvider _provider = new();

    public TokenViewerExtension(Action onDisposed)
    {
        _onDisposed = onDisposed;
    }

    public object? GetProvider(ProviderType providerType)
    {
        return providerType == ProviderType.Commands ? _provider : null;
    }

    public void Dispose()
    {
        _onDisposed();
    }
}
