// TokenViewerExtension contributors. Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Threading;
using System.Threading.Tasks;

namespace TokenViewerExtension;

/// <summary>某提供商账户余额（直连官方接口查询）。</summary>
internal sealed record AccountBalance(string DisplayName, string Text, bool IsAvailable, string? Error);

/// <summary>
/// 余额查询（已精简为仅 DeepSeek）：
/// 直接调用官方余额接口（GET /api.deepseek.com/user/balance），仅需 API Key。
/// </summary>
internal static class BalanceService
{
    /// <summary>该提供商是否支持余额查询。</summary>
    public static bool Supports(string providerId)
    {
        return providerId == "deepseek";
    }

    public static async Task<AccountBalance?> FetchAsync(ProviderConfig provider, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            return null;
        }

        try
        {
            var balance = await DeepSeekBalanceClient.GetAsync(provider.ApiKey, cancellationToken);
            return balance is null
                ? new AccountBalance(provider.DisplayName, "查询失败（API Key 无效或网络错误）", false, null)
                : new AccountBalance(provider.DisplayName, $"{balance.Currency} {balance.Total:0.##}（{(balance.IsAvailable ? "可用" : "不可用")}）", balance.IsAvailable, null);
        }
        catch
        {
            return new AccountBalance(provider.DisplayName, "查询失败", false, null);
        }
    }
}
