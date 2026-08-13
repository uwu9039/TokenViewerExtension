// TokenViewerExtension contributors. Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Text;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace TokenViewerExtension;

/// <summary>官方用量图表页（Markdown + ASCII 柱状图）；未配置官方数据源时显示获取指引。</summary>
internal sealed partial class UsageChartPage : ContentPage
{
    private readonly DirectPlatformInfo _platform;
    private readonly DirectUsageService _service;

    public UsageChartPage(DirectPlatformInfo platform, DirectUsageService service)
    {
        _platform = platform;
        _service = service;
        Title = $"{platform.DisplayName} 用量图表";
        Icon = new IconInfo("📈");
        Name = "查看";
    }

    public override IContent[] GetContent()
    {
        if (!_service.IsConfigured(_platform.Id))
        {
            return [new MarkdownContent(BuildSetupHelp())];
        }

        return [new MarkdownContent(ChartBuilder.BuildDailyChart(_platform, _service.GetSnapshot(_platform.Id)))];
    }

    /// <summary>未配置官方数据源时的配置指引。</summary>
    private string BuildSetupHelp()
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# {_platform.DisplayName} 用量图表");
        sb.AppendLine();
        sb.AppendLine("尚未配置官方数据源，因此没有图表可显示。");
        sb.AppendLine();
        sb.AppendLine("## 如何获取 DeepSeek 官方平台 Token");
        sb.AppendLine();
        sb.AppendLine("1. 浏览器登录 https://platform.deepseek.com");
        sb.AppendLine("2. 按 **F12** 打开开发者工具，切到 **Console（控制台）**");
        sb.AppendLine("3. 输入 `JSON.parse(localStorage.getItem('userToken')).value` 后回车");
        sb.AppendLine("4. 复制返回的 `eyJ...` 开头字符串");
        sb.AppendLine("5. 粘贴到扩展设置 → **DeepSeek 官方平台 Token**");
        sb.AppendLine();
        sb.AppendLine("> 该 Token 与 API Key 不同，**API Key 无法查询官方用量**；");
        sb.AppendLine("> Token 有效期数天，过期后在设置中重新更新即可。");
        sb.AppendLine();
        sb.AppendLine("> ⚠️ Token 相当于账户凭证，请勿泄露给他人。");
        return sb.ToString();
    }
}
