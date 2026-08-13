// TokenViewerExtension contributors. Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace TokenViewerExtension;

/// <summary>原始响应查看页：显示 DeepSeek 平台接口返回的原文（排查字段变更用）。</summary>
internal sealed partial class RawResponsePage : ContentPage
{
    private readonly string _raw;

    public RawResponsePage(string title, string raw)
    {
        _raw = raw;
        Title = $"{title} 原始响应";
        Icon = new IconInfo("🧾");
        Name = "查看";
    }

    public override IContent[] GetContent()
    {
        return [new MarkdownContent($"```json\n{_raw}\n```")];
    }
}
