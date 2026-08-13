// TokenViewerExtension contributors. Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TokenViewerExtension;

/// <summary>
/// 余额缓存：余额查询结果在多个页面共享，带 5 分钟 TTL 与并发去重，
/// 避免总览/明细/账户监控每 30 秒各自重复请求。
/// </summary>
internal sealed class BalanceCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private readonly object _lock = new();
    private readonly Dictionary<string, AccountBalance?> _cache = [];
    private readonly Dictionary<string, DateTime> _lastFetch = [];
    private readonly HashSet<string> _inflight = [];

    /// <summary>任一余额更新完成时触发。</summary>
    public event Action? Updated;

    /// <summary>
    /// 获取缓存的余额（可能为 null）；缓存过期时自动触发后台刷新（并发去重）。
    /// </summary>
    public AccountBalance? Get(ProviderConfig provider)
    {
        bool needsFetch;
        lock (_lock)
        {
            needsFetch = !(_cache.ContainsKey(provider.Id)
                && _lastFetch.TryGetValue(provider.Id, out var last)
                && DateTime.UtcNow - last < Ttl);
        }

        if (needsFetch)
        {
            TryRefresh(provider, force: false);
        }

        lock (_lock)
        {
            return _cache.TryGetValue(provider.Id, out var value) ? value : null;
        }
    }

    /// <summary>是否正在查询中（用于页面显示「查询中…」）。</summary>
    public bool IsPending(string providerId)
    {
        lock (_lock)
        {
            return _inflight.Contains(providerId);
        }
    }

    /// <summary>强制刷新（忽略 TTL）。</summary>
    public void Refresh(ProviderConfig provider)
    {
        TryRefresh(provider, force: true);
    }

    private void TryRefresh(ProviderConfig provider, bool force)
    {
        if (string.IsNullOrWhiteSpace(provider.ApiKey) || !BalanceService.Supports(provider.Id))
        {
            return;
        }

        lock (_lock)
        {
            if (!force && _inflight.Contains(provider.Id))
            {
                return;
            }

            if (force)
            {
                _cache.Remove(provider.Id);
                _lastFetch.Remove(provider.Id);
            }

            if (!_inflight.Add(provider.Id))
            {
                return;
            }
        }

        _ = Task.Run(async () =>
        {
            var balance = await BalanceService.FetchAsync(provider, CancellationToken.None);
            lock (_lock)
            {
                _cache[provider.Id] = balance;
                _lastFetch[provider.Id] = DateTime.UtcNow;
                _inflight.Remove(provider.Id);
            }

            Updated?.Invoke();
        });
    }
}
