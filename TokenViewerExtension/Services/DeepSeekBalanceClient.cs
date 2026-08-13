// TokenViewerExtension contributors. Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TokenViewerExtension;

/// <summary>DeepSeek 账户余额。</summary>
internal sealed record BalanceInfo(string Currency, double Total, bool IsAvailable);

/// <summary>查询 DeepSeek 账户余额（https://api.deepseek.com/user/balance）。</summary>
internal static class DeepSeekBalanceClient
{
    public static async Task<BalanceInfo?> GetAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.deepseek.com/user/balance");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var isAvailable = root.TryGetProperty("is_available", out var available) && available.GetBoolean();
            if (!root.TryGetProperty("balance_infos", out var infos) || infos.GetArrayLength() == 0)
            {
                return null;
            }

            var first = infos[0];
            var currency = first.TryGetProperty("currency", out var currencyProp) ? currencyProp.GetString() : null;
            var total = first.TryGetProperty("total_balance", out var totalProp) ? totalProp.GetString() : null;
            return new BalanceInfo(
                string.IsNullOrEmpty(currency) ? "CNY" : currency,
                double.TryParse(total, out var value) ? value : 0,
                isAvailable);
        }
        catch
        {
            return null;
        }
    }
}
