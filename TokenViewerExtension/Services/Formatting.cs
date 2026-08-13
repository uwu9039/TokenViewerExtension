// TokenViewerExtension contributors. Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;

namespace TokenViewerExtension;

/// <summary>显示格式化帮助类。</summary>
internal static class Formatting
{
    /// <summary>1234567 -> 1.23M，4567 -> 4.6K，123 -> 123。</summary>
    public static string Tokens(long count)
    {
        if (count >= 1_000_000)
        {
            return $"{(double)count / 1_000_000:0.##}M";
        }

        if (count >= 1_000)
        {
            return $"{(double)count / 1_000:0.#}K";
        }

        return count.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>费用显示：0.012 -> $0.01。</summary>
    public static string Cost(double cost)
    {
        if (cost <= 0)
        {
            return "$0";
        }

        return cost < 0.005 ? "≈$0" : $"${cost:0.##}";
    }

    /// <summary>yyyy-MM-dd -> MM-dd。</summary>
    public static string Day(string day)
    {
        return day.Length >= 10 ? day[5..] : day;
    }
}
