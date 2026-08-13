// TokenViewerExtension — 进程入口
// Copyright (c) TokenViewerExtension contributors. Licensed under the MIT License.

using System;
using System.Threading;
using Microsoft.CommandPalette.Extensions;
using Shmuelie.WinRTServer;
using Shmuelie.WinRTServer.CsWinRT;

namespace TokenViewerExtension;

/// <summary>
/// 程序入口。本扩展以 WinRT COM 服务器形式运行：
/// 命令面板宿主通过「-RegisterProcessAsComServer」参数启动本进程，
/// 然后按需请求 <see cref="TokenViewerExtension"/> 实例。
/// </summary>
internal static class Program
{
    [MTAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 0 || args[0] != "-RegisterProcessAsComServer")
        {
            Console.WriteLine("本程序由 PowerToys 命令面板以 COM 服务器方式启动，不支持直接运行。");
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
}
