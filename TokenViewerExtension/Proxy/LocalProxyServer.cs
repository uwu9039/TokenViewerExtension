// TokenViewerExtension contributors. Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace TokenViewerExtension;

/// <summary>
/// 本地代理服务器：为每个启用的提供商监听一个 127.0.0.1 端口，
/// 把所有请求转发给真实 API 并统计 token 用量。
/// </summary>
#pragma warning disable CA1001 // 生命周期与扩展进程一致，无需手动释放
internal sealed class LocalProxyServer
{
    private readonly TokenUsageRecorder _recorder;
    private readonly ProviderRegistry _registry;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    public bool IsRunning { get; private set; }

    public string? Error { get; private set; }

    /// <summary>代理启动/停止状态变化时触发。</summary>
    public event Action? StateChanged;

    public LocalProxyServer(TokenUsageRecorder recorder, ProviderRegistry registry)
    {
        _recorder = recorder;
        _registry = registry;
    }

    /// <summary>按当前配置（重新）启动代理。</summary>
    public void Restart()
    {
        Stop();

        var providers = _registry.Providers
            .Where(p => p.Enabled && !string.IsNullOrWhiteSpace(p.ApiKey) && !string.IsNullOrWhiteSpace(p.BaseUrl))
            .ToList();

        if (providers.Count == 0)
        {
            Error = "未启用任何提供商（请在设置中填写 API Key 并启用）";
            IsRunning = false;
            OnStateChanged();
            return;
        }

        try
        {
            var listener = new HttpListener();
            foreach (var provider in providers)
            {
                listener.Prefixes.Add($"http://127.0.0.1:{provider.Port}/");
            }

            listener.Start();
            _listener = listener;
            _cts = new CancellationTokenSource();
            IsRunning = true;
            Error = null;
            var token = _cts.Token;
            _ = Task.Run(() => RunLoopAsync(listener, token), token);
        }
        catch (Exception e)
        {
            IsRunning = false;
            Error = $"代理启动失败：{e.Message}（端口可能被占用，请修改设置）";
            try { _listener?.Close(); }
            catch { }
            _listener = null;
        }

        OnStateChanged();
    }

    public void Stop()
    {
        var listener = _listener;
        var cts = _cts;
        _listener = null;
        _cts = null;
        try { cts?.Cancel(); }
        catch { }
        try { listener?.Stop(); }
        catch { }
        try { listener?.Close(); }
        catch { }
        IsRunning = false;
    }

    private async Task RunLoopAsync(HttpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().WaitAsync(cancellationToken);
            }
            catch
            {
                break; // 代理已停止
            }

            var provider = _registry.FindByPort(context.Request.Url?.Port ?? 0);
            var token = cancellationToken;
            _ = Task.Run(() => ProxyHandler.HandleAsync(context, provider, _recorder, token), token);
        }
    }

    private void OnStateChanged()
    {
        StateChanged?.Invoke();
    }
}
