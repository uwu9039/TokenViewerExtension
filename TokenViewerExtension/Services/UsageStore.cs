// TokenViewerExtension contributors. Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TokenViewerExtension;

/// <summary>一条用量记录：某提供商某模型某天的累计 token。</summary>
internal sealed class ProviderUsageRecord
{
    public string ProviderId { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Day { get; set; } = string.Empty; // yyyy-MM-dd（本地时间）
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CachedTokens { get; set; }
    public long Requests { get; set; }
    public bool HasEstimates { get; set; }
}

internal sealed class UsageStoreData
{
    public List<ProviderUsageRecord> Records { get; set; } = [];
}

// 使用 source-generator 序列化，兼容 Release 构建的 AOT/裁剪
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(UsageStoreData))]
internal sealed partial class UsageJsonContext : JsonSerializerContext
{
}

/// <summary>用量持久化：内存字典 + JSON 文件落盘（每次请求后保存，避免丢失）。</summary>
internal sealed class UsageStore
{
    private readonly string _path;
    private readonly object _lock = new();
    private readonly Dictionary<(string Provider, string Model, string Day), ProviderUsageRecord> _records = [];

    private UsageStore(string path, IEnumerable<ProviderUsageRecord> initial)
    {
        _path = path;
        foreach (var record in initial)
        {
            _records[(record.ProviderId, record.Model, record.Day)] = record;
        }
    }

    public static UsageStore Load()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TokenViewerExtension");
        var path = Path.Combine(dir, "usage.json");
        try
        {
            // 确保目录存在，方便用户在诊断页中定位（文件本身只在代理记录请求后生成）
            Directory.CreateDirectory(dir);
        }
        catch
        {
        }

        return LoadFrom(path);
    }

    /// <summary>从指定路径加载（供测试注入路径使用）。</summary>
    public static UsageStore LoadFrom(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var data = JsonSerializer.Deserialize(File.ReadAllText(path), UsageJsonContext.Default.UsageStoreData);
                if (data is not null)
                {
                    // 只保留最近 120 天，控制文件体积
                    var cutoff = DateTime.Now.AddDays(-120).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    data.Records.RemoveAll(r => string.CompareOrdinal(r.Day, cutoff) < 0);
                    return new UsageStore(path, data.Records);
                }
            }
        }
        catch
        {
            // 文件损坏时从空数据开始，不影响扩展运行
        }

        return new UsageStore(path, []);
    }

    /// <summary>累加一条用量（线程安全）。</summary>
    public void Add(string providerId, string model, string day, long input, long output, long cached, long requests, bool hasEstimates)
    {
        lock (_lock)
        {
            var key = (providerId, model, day);
            if (!_records.TryGetValue(key, out var record))
            {
                record = new ProviderUsageRecord { ProviderId = providerId, Model = model, Day = day };
                _records[key] = record;
            }

            record.InputTokens += input;
            record.OutputTokens += output;
            record.CachedTokens += cached;
            record.Requests += requests;
            record.HasEstimates |= hasEstimates;
            SaveLocked();
        }
    }

    /// <summary>返回全部记录的快照。</summary>
    public ProviderUsageRecord[] GetRecords()
    {
        lock (_lock)
        {
            return _records.Values.ToArray();
        }
    }

    private void SaveLocked()
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var data = new UsageStoreData { Records = _records.Values.ToList() };
            var json = JsonSerializer.Serialize(data, UsageJsonContext.Default.UsageStoreData);
            File.WriteAllText(_path, json);
        }
        catch
        {
            // 落盘失败不阻塞代理请求
        }
    }
}
