// TokenViewerExtension contributors. Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace TokenViewerExtension;

/// <summary>当前配置的提供商（仅 DeepSeek）。</summary>
internal sealed record ProviderConfig(string Id, string DisplayName, string ApiKey, string ConsoleUrl, string IconEmoji);
