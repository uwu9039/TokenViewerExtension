// TokenViewerExtension contributors. Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace TokenViewerExtension;

/// <summary>
/// 扩展设置：DeepSeek API Key、官方平台 Token、界面刷新间隔。
/// 设置由命令面板宿主持久化；为防宿主设置随重启丢失，同时保存在本地
/// %LOCALAPPDATA%\TokenViewerExtension\settings.json，启动时自动恢复。
/// </summary>
internal sealed class SettingsManager
{
    private readonly Settings _settings;
    private readonly string _filePath;
    private readonly TextSetting _apiKeySetting;
    private readonly TextSetting _tokenSetting;
    private readonly TextSetting _refreshSetting;

    /// <summary>设置发生变化时触发（宿主 SettingsChanged 透传）。</summary>
    public event Action? Changed;

    public SettingsManager()
        : this(filePathOverride: null)
    {
    }

    /// <param name="filePathOverride">本地设置文件路径（供测试注入；生产环境传 null 使用默认位置）。</param>
    public SettingsManager(string? filePathOverride)
    {
        _settings = new Settings();

        _apiKeySetting = new TextSetting(
            "slot0_apiKey",
            "DeepSeek API Key",
            "你的真实 API Key（查询余额用）",
            string.Empty);
        _tokenSetting = new TextSetting(
            "deepseekPlatformToken",
            "DeepSeek 官方平台 Token",
            "登录 platform.deepseek.com → F12 → Console 输入 JSON.parse(localStorage.getItem('userToken')).value 回车复制（eyJ...，与 API Key 不同，有效期数天）",
            string.Empty);
        _refreshSetting = new TextSetting(
            "refreshInterval",
            "界面刷新间隔（秒）",
            "总览与明细页自动刷新的间隔，5-3600 秒",
            "30");

        _settings.Add(_apiKeySetting);
        _settings.Add(_tokenSetting);
        _settings.Add(_refreshSetting);

        // 本地持久化文件：防止宿主设置随重启丢失
        _filePath = filePathOverride
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TokenViewerExtension", "settings.json");

        _settings.SettingsChanged += (_, _) =>
        {
            SaveToFile();
            Rebuild();
            Changed?.Invoke();
        };

        LoadFromFile();
        Rebuild();
    }

    public Settings Settings => _settings;

    /// <summary>当前 DeepSeek 配置（随设置变化重建）。</summary>
    public ProviderConfig DeepSeek { get; private set; } = null!;

    /// <summary>DeepSeek 官方平台网页登录 Token（localStorage.userToken）。</summary>
    public string DeepSeekPlatformToken => _settings.GetSetting<string>("deepseekPlatformToken") ?? string.Empty;

    /// <summary>界面刷新间隔（秒）。</summary>
    public int RefreshIntervalSeconds { get; private set; } = 30;

    /// <summary>启动时从本地文件恢复设置（宿主设置丢失时兜底）。</summary>
    private void LoadFromFile()
    {
        var (apiKey, token, refresh) = SettingsStore.Load(_filePath);
        if (!string.IsNullOrEmpty(apiKey))
        {
            _apiKeySetting.Value = apiKey;
        }

        if (!string.IsNullOrEmpty(token))
        {
            _tokenSetting.Value = token;
        }

        if (!string.IsNullOrEmpty(refresh))
        {
            _refreshSetting.Value = refresh;
        }
    }

    /// <summary>把当前设置保存到本地文件。</summary>
    private void SaveToFile()
    {
        try
        {
            SettingsStore.Save(_filePath, _apiKeySetting.Value ?? string.Empty, _tokenSetting.Value ?? string.Empty, _refreshSetting.Value ?? string.Empty);
        }
        catch
        {
            // 落盘失败不影响使用
        }
    }

    private void Rebuild()
    {
        DeepSeek = new ProviderConfig(
            "deepseek",
            "DeepSeek",
            _settings.GetSetting<string>("slot0_apiKey") ?? string.Empty,
            "https://platform.deepseek.com/usage",
            "🐳");

        RefreshIntervalSeconds = int.TryParse(_settings.GetSetting<string>("refreshInterval"), out var seconds)
            ? Math.Clamp(seconds, 5, 3600)
            : 30;
    }
}
