// TokenViewerExtension contributors. Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Text.Json;

namespace TokenViewerExtension;

/// <summary>设置文件的读写（独立于宿主，保证重启后设置不丢失）。</summary>
internal static class SettingsStore
{
    /// <summary>把设置保存到本地文件。</summary>
    public static void Save(string filePath, string apiKey, string port, string platformToken, string refreshInterval)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("apiKey", apiKey);
            writer.WriteString("port", port);
            writer.WriteString("platformToken", platformToken);
            writer.WriteString("refreshInterval", refreshInterval);
            writer.WriteEndObject();
        }

        File.WriteAllBytes(filePath, stream.ToArray());
    }

    /// <summary>从本地文件读取设置；文件不存在或损坏时返回空字符串。</summary>
    public static (string ApiKey, string Port, string PlatformToken, string RefreshInterval) Load(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return (string.Empty, string.Empty, string.Empty, string.Empty);
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(filePath));
            var root = doc.RootElement;
            return (
                GetString(root, "apiKey"),
                GetString(root, "port"),
                GetString(root, "platformToken"),
                GetString(root, "refreshInterval"));
        }
        catch
        {
            return (string.Empty, string.Empty, string.Empty, string.Empty);
        }
    }

    private static string GetString(JsonElement root, string key)
    {
        return root.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString() ?? string.Empty
            : string.Empty;
    }
}
