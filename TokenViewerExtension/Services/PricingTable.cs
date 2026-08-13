// TokenViewerExtension contributors. Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Linq;

namespace TokenViewerExtension;

/// <summary>DeepSeek 模型单价表（每百万 token，USD）。仅用于费用估算，价格可能过时，仅供参考。</summary>
internal static class PricingTable
{
    private static readonly (string Model, double Input, double Output, double CacheRead)[] Prices =
    [
        // 先匹配更具体的模型名
        ("deepseek-chat", 0.27, 1.10, 0.07),
        ("deepseek-reasoner", 0.55, 2.19, 0.14),
        ("deepseek", 0.27, 1.10, 0.07),
    ];

    /// <summary>估算某模型、某次用量的费用（USD）。未收录的模型返回 0。</summary>
    public static double EstimateCost(string model, long input, long output, long cached)
    {
        foreach (var (modelName, inputPrice, outputPrice, cachePrice) in Prices)
        {
            if (model.Contains(modelName, System.StringComparison.OrdinalIgnoreCase))
            {
                return input / 1_000_000.0 * inputPrice
                    + output / 1_000_000.0 * outputPrice
                    + cached / 1_000_000.0 * cachePrice;
            }
        }

        return 0;
    }

    /// <summary>估算一组模型用量的总费用。</summary>
    public static double EstimateTotalCost(IEnumerable<ModelUsage> models)
    {
        return models.Sum(m => EstimateCost(m.Model, m.Input, m.Output, m.Cached));
    }
}
