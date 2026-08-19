using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Cortex.Core.Auth;
using Cortex.Core.Objects;
using Microsoft.Extensions.Options;

namespace Cortex.Core.Providers;

/// <summary>
/// Direct Google Gemini connector (generativelanguage.googleapis.com). Uses
/// streamGenerateContent with alt=sse; functionCall parts arrive complete (no
/// deltas) and are normalized into <see cref="ChatChunk.ToolCall"/> chunks.
/// </summary>
public sealed class GeminiProvider : IProvider
{
    public const string HttpClientName = "gemini";

    private readonly IHttpClientFactory _httpFactory;
    private readonly ProviderOptions.ProviderEndpoint _endpoint;

    public GeminiProvider(IHttpClientFactory factory, IOptions<ProviderOptions> opts)
    {
        _httpFactory = factory;
        _endpoint = opts.Value.Gemini;
    }

    public ChatProviderKind Kind => ChatProviderKind.Gemini;

    private HttpClient CreateClient(ProviderCallContext? context)
    {
        var http = _httpFactory.CreateClient(HttpClientName);
        var baseUrl = !string.IsNullOrWhiteSpace(context?.BaseUrl) ? context.BaseUrl : _endpoint.BaseUrl;
        http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        var key = !string.IsNullOrWhiteSpace(context?.ApiKey) ? context.ApiKey : _endpoint.KeyConfigured ? _endpoint.ApiKey : null;
        if (!string.IsNullOrEmpty(key))
            http.DefaultRequestHeaders.Add("x-goog-api-key", key);
        return http;
    }

    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(ProviderCallContext? context = null, CancellationToken ct = default)
    {
        using var http = CreateClient(context);
        var res = await http.GetAsync("models", ct);
        res.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var list = new List<ModelInfo>();
        if (doc.RootElement.TryGetProperty("models", out var models))
        {
            foreach (var m in models.EnumerateArray())
            {
                var name = m.GetProperty("name").GetString()!;
                var id = name.StartsWith("models/") ? name["models/".Length..] : name;
                // Skip embedders and other non-generateContent models.
                if (m.TryGetProperty("supportedGenerationMethods", out var methods))
                {
                    var supports = false;
                    foreach (var mm in methods.EnumerateArray())
                        if (mm.GetString() == "generateContent")
                            supports = true;
                    if (!supports) continue;
                }
                var meta = ModelCatalog.Find(Kind, id);
                list.Add(new ModelInfo(
                    Id: id,
                    Name: m.TryGetProperty("displayName", out var dn) && !string.IsNullOrEmpty(dn.GetString()) ? dn.GetString()! : id,
                    Description: m.TryGetProperty("description", out var d) ? d.GetString() : null,
                    ContextLength: m.TryGetProperty("inputTokenLimit", out var tl) && tl.TryGetInt32(out var tlv) ? tlv : meta?.ContextLength,
                    PromptPrice: meta?.PromptPrice,
                    CompletionPrice: meta?.CompletionPrice,
                    SupportsTools: meta?.SupportsTools,
                    SupportsVision: meta?.SupportsVision));
            }
        }
        return list;
    }

    public async IAsyncEnumerable<ChatChunk> StreamChatAsync(
        ChatRequestPayload payload,
        ProviderCallContext? context = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var http = CreateClient(context);
        using var req = new HttpRequestMessage(HttpMethod.Post, $"models/{payload.Model}:streamGenerateContent?alt=sse")
        {
            Content = JsonContent.Create(BuildBody(payload))
        };
        using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!res.IsSuccessStatusCode)
        {
            var text = await res.Content.ReadAsStringAsync(ct);
            yield return new ChatChunk.Error($"Gemini {res.StatusCode}: {text}");
            yield break;
        }

        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var sr = new StreamReader(stream);

        int? promptTokens = null;
        int? completionTokens = null;
        var callIndex = 0;

        while (!sr.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await sr.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            using var doc = JsonDocument.Parse(line["data: ".Length..]);
            var root = doc.RootElement;

            if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0 &&
                candidates[0].TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts))
            {
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var tx) && tx.ValueKind == JsonValueKind.String)
                    {
                        var text = tx.GetString();
                        if (!string.IsNullOrEmpty(text))
                            yield return new ChatChunk.Token(text);
                    }
                    if (part.TryGetProperty("functionCall", out var fc))
                    {
                        var name = fc.GetProperty("name").GetString() ?? "";
                        var args = fc.TryGetProperty("args", out var a) ? a.GetRawText() : "{}";
                        yield return new ChatChunk.ToolCall($"call_{callIndex++}", name, args);
                    }
                }
            }

            if (root.TryGetProperty("usageMetadata", out var usage))
            {
                if (usage.TryGetProperty("promptTokenCount", out var ptc) && ptc.TryGetInt32(out var ptcv)) promptTokens = ptcv;
                if (usage.TryGetProperty("candidatesTokenCount", out var ctc) && ctc.TryGetInt32(out var ctcv)) completionTokens = ctcv;
            }
        }

        yield return new ChatChunk.Usage(promptTokens, completionTokens);
        yield return new ChatChunk.Done();
    }

    private static Dictionary<string, object?> BuildBody(ChatRequestPayload payload)
    {
        var system = string.Join("\n\n", payload.Messages
            .Where(m => m.Role == MessageRole.System)
            .Select(m => m.Content));

        var contents = new List<object>();
        foreach (var m in payload.Messages)
        {
            switch (m.Role)
            {
                case MessageRole.System:
                    continue;
                case MessageRole.Tool:
                    // Gemini matches tool responses by function name, not call id.
                    var fnName = m.ToolName ?? m.ToolCallId ?? "";
                    contents.Add(new Dictionary<string, object?>
                    {
                        ["role"] = "user",
                        ["parts"] = new object[]
                        {
                            new Dictionary<string, object?>
                            {
                                ["functionResponse"] = new Dictionary<string, object?>
                                {
                                    ["name"] = fnName,
                                    ["response"] = ParseJsonOr(m.Content, new Dictionary<string, object?> { ["result"] = m.Content })
                                }
                            }
                        }
                    });
                    break;
                case MessageRole.Assistant when m.ToolCalls is { Count: > 0 }:
                    var parts = new List<object?>();
                    if (!string.IsNullOrEmpty(m.Content))
                        parts.Add(new Dictionary<string, object?> { ["text"] = m.Content });
                    parts.AddRange(m.ToolCalls.Select(tc => new Dictionary<string, object?>
                    {
                        ["functionCall"] = new Dictionary<string, object?>
                        {
                            ["name"] = tc.Name,
                            ["args"] = ParseJsonOr(tc.ArgumentsJson, new Dictionary<string, object?>())
                        }
                    }));
                    contents.Add(new Dictionary<string, object?>
                    {
                        ["role"] = "model",
                        ["parts"] = parts
                    });
                    break;
                default:
                    contents.Add(new Dictionary<string, object?>
                    {
                        ["role"] = m.Role == MessageRole.Assistant ? "model" : "user",
                        ["parts"] = new object[] { new Dictionary<string, object?> { ["text"] = m.Content } }
                    });
                    break;
            }
        }

        var body = new Dictionary<string, object?>
        {
            ["contents"] = contents,
            ["generationConfig"] = new Dictionary<string, object?>
            {
                ["temperature"] = payload.Temperature,
                ["maxOutputTokens"] = payload.MaxTokens
            }
        };
        if (!string.IsNullOrEmpty(system))
            body["systemInstruction"] = new Dictionary<string, object?>
            {
                ["parts"] = new object[] { new Dictionary<string, object?> { ["text"] = system } }
            };
        if (payload.Tools is { Count: > 0 })
        {
            body["tools"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["functionDeclarations"] = payload.Tools.Select(t => new Dictionary<string, object?>
                    {
                        ["name"] = t.Name,
                        ["description"] = t.Description,
                        ["parameters"] = ParseJsonOr(t.ParametersJson, new Dictionary<string, object?> { ["type"] = "object" })
                    }).ToList()
                }
            };
        }
        return body;
    }

    private static JsonElement? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonDocument.Parse(json).RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static object ParseJsonOr(string? json, object fallback) => ParseJson(json) ?? fallback;
}
