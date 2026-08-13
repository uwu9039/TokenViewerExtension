// TokenViewerExtension contributors. Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace TokenViewerExtension;

/// <summary>
/// ASCII 图表生成（纯函数，便于测试）：基于官方平台快照生成
/// 每日用量柱状图 + 模型占比图，输出 Markdown 代码块。
/// </summary>
internal static class ChartBuilder
{
    private const int BarMax = 24;

    public static string BuildDailyChart(DirectPlatformInfo platform, DirectPlatformSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return $"暂无数据：请先点击「立即刷新」拉取 {platform.DisplayName} 官方数据。";
        }

        if (snapshot.Error is not null)
        {
            return $"拉取失败：{snapshot.Error}";
        }

        // 按天与模型都没有数据才视为空（按天可能延迟为空，但模型汇总有值）
        if (snapshot.TotalTokens == 0)
        {
            return "本月暂无用量记录。";
        }

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# {platform.DisplayName} 用量图表（官方数据）");
        sb.AppendLine();

        if (!snapshot.DaysHaveData)
        {
            // 官方按天统计未更新（全 0）：跳过每日柱状图，仅展示月度汇总与模型占比
            sb.AppendLine("> 按天数据官方尚未更新（统计延迟），以下为**月度汇总**：");
            sb.AppendLine();
            var monthText = $"月总计 {Formatting.Tokens(snapshot.TotalTokens)} tokens · {snapshot.MonthRequests} 次请求"
                + (snapshot.TotalCost is > 0 ? $" · 消费 {Formatting.Cost(snapshot.TotalCost.Value)}" : string.Empty);
            sb.AppendLine(monthText);
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("```");
            var max = snapshot.Days.Max(d => d.Input + d.Output + d.Cached);
            sb.AppendLine(CultureInfo.InvariantCulture, $"{"日期",5} {"输入",10} {"输出",10}  {"总计",12}");
            foreach (var day in snapshot.Days.Take(31))
            {
                var total = day.Input + day.Output + day.Cached;
                var bar = Bar(max, total);
                sb.AppendLine(CultureInfo.InvariantCulture, $"{Formatting.Day(day.Day),5} {Formatting.Tokens(day.Input),10} {Formatting.Tokens(day.Output),10}  {bar} {Formatting.Tokens(total)}");
            }

            var costText = snapshot.TotalCost is > 0 ? $" · 消费 {Formatting.Cost(snapshot.TotalCost.Value)}" : string.Empty;
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"月总计 {Formatting.Tokens(snapshot.TotalTokens)} tokens · {snapshot.MonthRequests} 次请求{costText}");
            sb.AppendLine("```");
        }

        if (snapshot.Models.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"## 模型占比（本月）");
            sb.AppendLine();
            sb.AppendLine("```");
            var modelMax = snapshot.Models.Max(m => m.Input + m.Output + m.Cached);
            var all = snapshot.Models.Sum(m => m.Input + m.Output + m.Cached);
            foreach (var model in snapshot.Models.Take(10))
            {
                var total = model.Input + model.Output + model.Cached;
                var pct = all > 0 ? (double)total / all * 100 : 0;
                sb.AppendLine(CultureInfo.InvariantCulture, $"{model.Model,-32} {Bar(modelMax, total),-26} {pct:0.#}%");
            }

            sb.AppendLine("```");
        }

        return sb.ToString();
    }

    private static string Bar(long max, long value)
    {
        var length = max > 0 ? (int)Math.Round((double)value / max * BarMax) : 0;
        return new string('█', length);
    }
}
