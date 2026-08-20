using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Cortex.Core.Auth;
using Cortex.Core.Objects;
using Microsoft.Extensions.Options;

namespace Cortex.Core.Providers;

/// <summary>
/// Direct Anthropic connector (claude models). Uses the Messages API
/// (POST /v1/messages) with its typed SSE event stream; tool_use blocks are
/// normalized into <see cref="ChatChunk.ToolCall"/> chunks.
/// </summary>
public sealed class AnthropicProvider : IProvider
{
    public const string HttpClientName = "anthropic";
    private const string Version = "2023-06-01";

    private readonly IHttpClientFactory _httpFactory;
    private readonly ProviderOptions.ProviderEndpoint _endpoint;

    public AnthropicProvider(IHttpClientFactory factory, IOptions<ProviderOptions> opts)
    {
        _httpFactory = factory;
        _endpoint = opts.Value.Anthropic;
    }

    public ChatProviderKind Kind => ChatProviderKind.Anthropic;

    private HttpClient CreateClient(ProviderCallContext? context)
    {
        var http = _httpFactory.CreateClient(HttpClientName);
        var baseUrl = !string.IsNullOrWhiteSpace(context?.BaseUrl) ? context.BaseUrl : _endpoint.BaseUrl;
        http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        var key = !string.IsNullOrWhiteSpace(context?.ApiKey) ? context.ApiKey : _endpoint.KeyConfigured ? _endpoint.ApiKey : null;
        if (!string.IsNullOrEmpty(key))
        {
            http.DefaultRequestHeaders.Add("x-api-key", key);
            http.DefaultRequestHeaders.Add("anthropic-version", Version);
        }
        return http;
    }

    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(ProviderCallContext? context = null, CancellationToken ct = default)
    {
        using var http = CreateClient(context);
        var res = await http.GetAsync("v1/models", ct);
        res.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var list = new List<ModelInfo>();
        if (doc.RootElement.TryGetProperty("data", out var data))
        {
            foreach (var el in data.EnumerateArray())
            {
                var id = el.GetProperty("id").GetString()!;
                var meta = ModelCatalog.Find(Kind, id);
                list.Add(new ModelInfo(
                    Id: id,
                    Name: el.TryGetProperty("display_name", out var dn) && !string.IsNullOrEmpty(dn.GetString()) ? dn.GetString()! : id,
                    Description: null,
                    ContextLength: meta?.ContextLength,
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
        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
        {
            Content = JsonContent.Create(BuildBody(payload))
        };
        using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!res.IsSuccessStatusCode)
        {
            var text = await res.Content.ReadAsStringAsync(ct);
            yield return new ChatChunk.Error($"Anthropic {res.StatusCode}: {text}");
            yield break;
        }

        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var sr = new StreamReader(stream);

        int? promptTokens = null;
        int? completionTokens = null;
        // Anthropic streams a tool call as content_block_start (id+name) followed by
        // input_json_delta parts; flush on content_block_stop.
        (string Id, string Name)? pendingTool = null;
        var pendingArgs = new System.Text.StringBuilder();
        string? eventType = null;

        while (!sr.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await sr.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line)) continue;

            if (line.StartsWith("event: "))
            {
                eventType = line["event: ".Length..].Trim();
                continue;
            }
            if (!line.StartsWith("data: ")) continue;

            using var doc = JsonDocument.Parse(line["data: ".Length..]);
            var root = doc.RootElement;

            switch (eventType)
            {
                case "message_start":
                    if (root.TryGetProperty("message", out var msg) &&
                        msg.TryGetProperty("usage", out var u1) &&
                        u1.TryGetProperty("input_tokens", out var it) && it.TryGetInt32(out var itv))
                        promptTokens = itv;
                    break;

                case "content_block_start":
                    if (root.TryGetProperty("content_block", out var block) &&
                        block.TryGetProperty("type", out var bt) && bt.GetString() == "tool_use")
                    {
                        pendingTool = (block.GetProperty("id").GetString()!, block.GetProperty("name").GetString()!);
                        pendingArgs.Clear();
                    }
                    break;

                case "content_block_delta":
                    if (root.TryGetProperty("delta", out var delta))
                    {
                        var type = delta.TryGetProperty("type", out var dt) ? dt.GetString() : null;
                        if (type == "thinking_delta" && delta.TryGetProperty("thinking", out var th))
                        {
                            var thinking = th.GetString();
                            if (!string.IsNullOrEmpty(thinking))
                                yield return new ChatChunk.Reasoning(thinking);
                        }
                        else if (type == "text_delta" && delta.TryGetProperty("text", out var tx))
                        {
                            var text = tx.GetString();
                            if (!string.IsNullOrEmpty(text))
                                yield return new ChatChunk.Token(text);
                        }
                        else if (type == "input_json_delta" && pendingTool is not null &&
                                 delta.TryGetProperty("partial_json", out var pj))
                        {
                            pendingArgs.Append(pj.GetString());
                        }
                    }
                    break;

                case "content_block_stop":
                    if (pendingTool is { } tool)
                    {
                        yield return new ChatChunk.ToolCall(tool.Id, tool.Name, pendingArgs.ToString());
                        pendingTool = null;
                        pendingArgs.Clear();
                    }
                    break;

                case "message_delta":
                    if (root.TryGetProperty("usage", out var u2) &&
                        u2.TryGetProperty("output_tokens", out var ot) && ot.TryGetInt32(out var otv))
                        completionTokens = otv;
                    break;

                case "message_stop":
                    yield return new ChatChunk.Usage(promptTokens, completionTokens);
                    yield return new ChatChunk.Done();
                    yield break;

                case "error":
                    var message = root.TryGetProperty("error", out var err) && err.TryGetProperty("message", out var em)
                        ? em.GetString()
                        : "Anthropic stream error";
                    yield return new ChatChunk.Error(message ?? "Anthropic stream error");
                    break;
            }
        }

        yield return new ChatChunk.Usage(promptTokens, completionTokens);
        yield return new ChatChunk.Done();
    }

    private static Dictionary<string, object?> BuildBody(ChatRequestPayload payload)
    {
        // Anthropic requires max_tokens; system prompts go in a dedicated field.
        var system = string.Join("\n\n", payload.Messages
            .Where(m => m.Role == MessageRole.System)
            .Select(m => m.Content));

        var messages = new List<object>();
        foreach (var m in payload.Messages)
        {
            switch (m.Role)
            {
                case MessageRole.System:
                    continue;
                case MessageRole.Tool:
                    messages.Add(new Dictionary<string, object?>
                    {
                        ["role"] = "user",
                        ["content"] = new object[]
                        {
                            new Dictionary<string, object?>
                            {
                                ["type"] = "tool_result",
                                ["tool_use_id"] = m.ToolCallId,
                                ["content"] = m.Content
                            }
                        }
                    });
                    break;
                case MessageRole.Assistant when m.ToolCalls is { Count: > 0 }:
                    var blocks = new List<object?>
                    {
                        new Dictionary<string, object?> { ["type"] = "text", ["text"] = m.Content }
                    };
                    blocks.AddRange(m.ToolCalls.Select(tc => new Dictionary<string, object?>
                    {
                        ["type"] = "tool_use",
                        ["id"] = tc.Id,
                        ["name"] = tc.Name,
                        ["input"] = ParseJsonOr(tc.ArgumentsJson, new Dictionary<string, object?>())
                    }));
                    messages.Add(new Dictionary<string, object?>
                    {
                        ["role"] = "assistant",
                        ["content"] = blocks
                    });
                    break;
                default:
                    messages.Add(new Dictionary<string, object?>
                    {
                        ["role"] = m.Role == MessageRole.Assistant ? "assistant" : "user",
                        ["content"] = m.Content
                    });
                    break;
            }
        }

        var body = new Dictionary<string, object?>
        {
            ["model"] = payload.Model,
            ["max_tokens"] = payload.MaxTokens ?? 4096,
            ["messages"] = messages,
            ["stream"] = true,
            ["temperature"] = payload.Temperature
        };
        if (!string.IsNullOrEmpty(system))
            body["system"] = system;
        if (payload.Tools is { Count: > 0 })
        {
            body["tools"] = payload.Tools.Select(t => new Dictionary<string, object?>
            {
                ["name"] = t.Name,
                ["description"] = t.Description,
                ["input_schema"] = ParseJsonOr(t.ParametersJson, new Dictionary<string, object?> { ["type"] = "object" })
            }).ToList();
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
