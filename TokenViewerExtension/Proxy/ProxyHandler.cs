// TokenViewerExtension contributors. Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TokenViewerExtension;

/// <summary>
/// 代理请求处理器：把客户端的请求转发给真实厂商 API，
/// 从响应（JSON 或 SSE）中解析 usage 并记录 token 用量。
/// </summary>
internal static class ProxyHandler
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

    // 逐跳（hop-by-hop）头部不转发，由本代理自行管理
    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "connection", "keep-alive", "proxy-authenticate", "proxy-authorization",
        "te", "trailer", "transfer-encoding", "upgrade", "host",
        "content-length", "expect", "accept-encoding",
    };

    public static async Task HandleAsync(HttpListenerContext context, ProviderConfig? provider, TokenUsageRecorder recorder, CancellationToken cancellationToken)
    {
        try
        {
            if (provider is null)
            {
                await WriteJsonErrorAsync(context, 404, "未找到该端口对应的提供商配置，请在扩展设置中检查");
                return;
            }

            await ForwardAsync(context, provider, recorder, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            try
            {
                await WriteJsonErrorAsync(context, 502, $"代理转发失败：{e.Message}");
            }
            catch
            {
            }
        }
        finally
        {
            try { context.Response.Close(); }
            catch { }
        }
    }

    private static async Task ForwardAsync(HttpListenerContext context, ProviderConfig provider, TokenUsageRecorder recorder, CancellationToken cancellationToken)
    {
        var requestBody = await ReadRequestBodyAsync(context.Request);
        var url = context.Request.Url;

        // 路径规范化：客户端通常按 OpenAI 惯例访问 /v1/xxx，
        // 而提供商 Base URL 本身也可能带 /v1（如 https://api.deepseek.com/v1），
        // 需要去掉客户端路径里的 /v1 前缀避免重复（如 /v1/v1/chat/completions）。
        var path = url?.AbsolutePath ?? "/";
        if (path.StartsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            path = path.Length > 3 ? path[3..] : "/";
        }

        var upstreamUrl = provider.BaseUrl.TrimEnd('/') + path + url?.Query;

        using var request = new HttpRequestMessage(new HttpMethod(context.Request.HttpMethod), upstreamUrl);
        CopyRequestHeaders(context.Request.Headers, request);
        // 用设置中保存的真实密钥转发（覆盖客户端带来的 Authorization）
        if (!string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        }

        // 禁用压缩，保证 SSE 可以按行解析
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");

        if (requestBody.Length > 0)
        {
            request.Content = new ByteArrayContent(requestBody);
            if (!string.IsNullOrEmpty(context.Request.ContentType))
            {
                request.Content.Headers.TryAddWithoutValidation("Content-Type", context.Request.ContentType);
            }
        }

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;

        context.Response.StatusCode = (int)response.StatusCode;
        CopyResponseHeaders(response, context.Response);

        var isSse = contentType.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase);
        if (response.IsSuccessStatusCode && isSse)
        {
            await ForwardStreamAsync(context.Response, response, requestBody, provider, recorder, cancellationToken);
        }
        else
        {
            var responseBody = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (response.IsSuccessStatusCode && contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            {
                TryRecordJson(responseBody, requestBody, provider, recorder);
            }

            await context.Response.OutputStream.WriteAsync(responseBody, cancellationToken);
        }
    }

    /// <summary>SSE 流式透传：逐行转发，同时解析 usage / 累计输出文本用于估算。</summary>
    private static async Task ForwardStreamAsync(
        HttpListenerResponse response,
        HttpResponseMessage upstream,
        byte[] requestBody,
        ProviderConfig provider,
        TokenUsageRecorder recorder,
        CancellationToken cancellationToken)
    {
        response.ContentType = "text/event-stream";
        response.Headers["Cache-Control"] = "no-cache";
        response.SendChunked = true;

        await using var upstreamStream = await upstream.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(upstreamStream, Encoding.UTF8);
        await using var writer = new StreamWriter(response.OutputStream, new UTF8Encoding(false)) { AutoFlush = true };

        var (requestModel, estimatedInputTokens) = AnalyzeRequest(requestBody);
        var estimatedOutputChars = 0L;
        long? usageInput = null, usageOutput = null, usageCached = null;
        string? streamModel = requestModel;

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            await writer.WriteLineAsync(line);
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var payload = line[5..].Trim();
            if (payload.Length == 0 || payload == "[DONE]")
            {
                if (payload == "[DONE]")
                {
                    break;
                }

                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;
                if (root.TryGetProperty("model", out var modelProp))
                {
                    streamModel = modelProp.GetString();
                }

                if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
                {
                    usageInput = GetLong(usage, "prompt_tokens", "input_tokens");
                    usageOutput = GetLong(usage, "completion_tokens", "output_tokens");
                    usageCached = GetLong(usage, "prompt_cache_hit_tokens", "cache_read_input_tokens", "cache_creation_input_tokens", "cached_tokens");
                }
                else if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
                {
                    var first = choices[0];
                    if (first.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.Object)
                    {
                        if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                        {
                            estimatedOutputChars += content.GetString()!.Length;
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // 忽略无法解析的块（如心跳）
            }
        }

        if (usageInput is not null || usageOutput is not null)
        {
            recorder.Record(provider.Id, streamModel ?? "未知模型", usageInput ?? 0, usageOutput ?? 0, usageCached ?? 0, hasEstimates: false);
        }
        else
        {
            // 流式响应未带 usage 时按字符数估算（约为 4 字符/token）
            var estimatedOutput = Math.Max(1, estimatedOutputChars / 4);
            recorder.Record(provider.Id, streamModel ?? "未知模型", estimatedInputTokens, estimatedOutput, 0, hasEstimates: true);
        }
    }

    /// <summary>从 JSON 响应体中解析 usage 并记录。</summary>
    private static void TryRecordJson(byte[] responseBody, byte[] requestBody, ProviderConfig provider, TokenUsageRecorder recorder)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
            {
                return; // 如 GET /v1/models 等无用量接口，不记录
            }

            var input = GetLong(usage, "prompt_tokens", "input_tokens");
            var output = GetLong(usage, "completion_tokens", "output_tokens");
            var cached = GetLong(usage, "prompt_cache_hit_tokens", "cache_read_input_tokens", "cache_creation_input_tokens", "cached_tokens");
            var model = root.TryGetProperty("model", out var modelProp) ? modelProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(model))
            {
                var (requestModel, _) = AnalyzeRequest(requestBody);
                model = requestModel;
            }

            recorder.Record(provider.Id, model ?? "未知模型", input, output, cached, hasEstimates: false);
        }
        catch (JsonException)
        {
            // 非 JSON 响应不记录
        }
    }

    /// <summary>从请求体中读取模型名，并按消息文本估算输入 token（用于流式兜底）。</summary>
    private static (string? Model, long EstimatedInputTokens) AnalyzeRequest(byte[] requestBody)
    {
        if (requestBody.Length == 0)
        {
            return (null, 0);
        }

        try
        {
            using var doc = JsonDocument.Parse(requestBody);
            var root = doc.RootElement;
            var model = root.TryGetProperty("model", out var modelProp) ? modelProp.GetString() : null;

            long chars = 0;
            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messages.EnumerateArray())
                {
                    if (message.TryGetProperty("content", out var content))
                    {
                        CountContentChars(content, ref chars);
                    }
                }
            }

            // embeddings：input 可能是字符串数组或 token id 数组
            if (root.TryGetProperty("input", out var input) && input.ValueKind is JsonValueKind.String or JsonValueKind.Array)
            {
                if (input.ValueKind == JsonValueKind.String)
                {
                    chars += input.GetString()!.Length;
                }
                else
                {
                    foreach (var item in input.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            chars += item.GetString()!.Length;
                        }
                        else if (item.ValueKind == JsonValueKind.Number)
                        {
                            chars += 4; // token id 数组：每个 id 约等于 1 个 token
                        }
                    }
                }
            }

            return (model, Math.Max(1, chars / 4));
        }
        catch (JsonException)
        {
            return (null, 0);
        }
    }

    private static void CountContentChars(JsonElement content, ref long chars)
    {
        if (content.ValueKind == JsonValueKind.String)
        {
            chars += content.GetString()!.Length;
        }
        else if (content.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in content.EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.Object && part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    chars += text.GetString()!.Length;
                }
            }
        }
    }

    private static async Task<byte[]> ReadRequestBodyAsync(HttpListenerRequest request)
    {
        if (!request.HasEntityBody)
        {
            return [];
        }

        using var stream = new MemoryStream();
        await request.InputStream.CopyToAsync(stream);
        return stream.ToArray();
    }

    private static void CopyRequestHeaders(System.Collections.Specialized.NameValueCollection source, HttpRequestMessage target)
    {
        foreach (string? name in source.AllKeys)
        {
            if (string.IsNullOrEmpty(name) || HopByHopHeaders.Contains(name))
            {
                continue;
            }

            if (name.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            {
                continue; // 统一使用设置中保存的密钥
            }

            var values = source.GetValues(name);
            if (values is null)
            {
                continue;
            }

            target.Headers.TryAddWithoutValidation(name, values);
        }
    }

    private static void CopyResponseHeaders(HttpResponseMessage source, HttpListenerResponse target)
    {
        target.ContentType = source.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
        foreach (var header in source.Headers)
        {
            if (HopByHopHeaders.Contains(header.Key) || header.Key.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            target.Headers[header.Key] = string.Join(",", header.Value);
        }
    }

    private static async Task WriteJsonErrorAsync(HttpListenerContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        // 手写 JSON，避免反射序列化破坏 Release 的 AOT/裁剪
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("error");
            writer.WriteStartObject();
            writer.WriteString("message", message);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        await context.Response.OutputStream.WriteAsync(stream.ToArray());
    }

    private static long GetLong(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number)
            {
                return prop.GetInt64();
            }
        }

        return 0;
    }

    private static HttpClient CreateHttpClient()
    {
        // 流式响应可能持续较长时间，不能套默认 100 秒超时
        return new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    }
}
