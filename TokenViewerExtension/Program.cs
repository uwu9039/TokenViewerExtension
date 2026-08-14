// TokenViewerExtension — 进程入口
// Copyright (c) TokenViewerExtension contributors. Licensed under the MIT License.

using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.CommandPalette.Extensions;
using Shmuelie.WinRTServer;
using Shmuelie.WinRTServer.CsWinRT;

namespace TokenViewerExtension;

/// <summary>
/// 程序入口。本扩展以 WinRT COM 服务器形式运行：
/// 命令面板宿主通过「-RegisterProcessAsComServer」参数启动本进程，
/// 然后按需请求 <see cref="TokenViewerExtension"/> 实例。
/// 直接启动（无该参数）时显示说明窗口，避免被误判为启动崩溃。
/// </summary>
internal static class Program
{
    [MTAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 0 || args[0] != "-RegisterProcessAsComServer")
        {
            // 用户/认证测试直接启动 exe 时：给出可见反馈，而不是立即退出
            MessageBox(
                IntPtr.Zero,
                "TokenViewerExtension 是 PowerToys 命令面板扩展。\n\n请打开 PowerToys 命令面板（默认 Alt+Space）使用本扩展。\n\nThis is a PowerToys Command Palette extension. Open the Command Palette to use it.",
                "TokenViewerExtension",
                0x40 | 0x1000); // MB_ICONINFORMATION | MB_SYSTEMMODAL
            return;
        }

        var server = new ComServer();
        using var exitSignal = new ManualResetEventSlim(initialState: false);

        // 整个进程只维护一个扩展实例，宿主每次激活都复用同一对象
        var extension = new TokenViewerExtension(() => exitSignal.Set());
        server.RegisterClass<TokenViewerExtension, IExtension>(() => extension);

        server.Start();

        // 阻塞直到宿主释放扩展（触发 Dispose）
        exitSignal.Wait();

        server.Stop();
        server.UnsafeDispose();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
