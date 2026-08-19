using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Cortex.Core.Auth;
using Cortex.Core.Objects;

namespace Cortex.Core.Providers;

/// <summary>
/// OpenAI-compatible provider: chat/completions over SSE ("data:" lines with a
/// "[DONE]" sentinel) and a "models" listing endpoint. Shared by OpenRouter,
/// OpenAI, xAI, Mistral, DeepSeek and LM Studio; subclasses set base URL, auth
/// and provider-specific headers. Tool-call deltas are accumulated per index and
/// emitted as normalized <see cref="ChatChunk.ToolCall"/> chunks.
/// </summary>
public abstract class OpenAiCompatibleProvider : IProvider
{
    protected ProviderOptions.ProviderEndpoint Endpoint { get; }
    private readonly IHttpClientFactory _httpFactory;
    private readonly string _httpClientName;
    private readonly string _errorPrefix;

    protected OpenAiCompatibleProvider(
        IHttpClientFactory factory,
        string httpClientName,
        ProviderOptions.ProviderEndpoint endpoint,
        string errorPrefix)
    {
        _httpFactory = factory;
        _httpClientName = httpClientName;
        Endpoint = endpoint;
        _errorPrefix = errorPrefix;
    }

    public abstract ChatProviderKind Kind { get; }

    /// <summary>Hook for provider-specific default headers/timeouts on each client.</summary>
    protected virtual void OnClientCreated(HttpClient http) { }

    /// <summary>Creates a per-call client: context overrides (BYOK key, custom base URL)
    /// win over server configuration.</summary>
    protected HttpClient CreateClient(ProviderCallContext? context)
    {
        var http = _httpFactory.CreateClient(_httpClientName);
        var baseUrl = !string.IsNullOrWhiteSpace(context?.BaseUrl) ? context.BaseUrl : Endpoint.BaseUrl;
        http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        var key = !string.IsNullOrWhiteSpace(context?.ApiKey) ? context.ApiKey : Endpoint.KeyConfigured ? Endpoint.ApiKey : null;
        if (!string.IsNullOrEmpty(key))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        OnClientCreated(http);
        return http;
    }

    public virtual async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(ProviderCallContext? context = null, CancellationToken ct = default)
    {
        using var http = CreateClient(context);
        var res = await http.GetAsync("models", ct);
        res.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var list = new List<ModelInfo>();
        foreach (var el in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            var id = el.GetProperty("id").GetString()!;
            var meta = ModelCatalog.Find(Kind, id);
            list.Add(new ModelInfo(
                Id: id,
                Name: el.TryGetProperty("name", out var n) && !string.IsNullOrEmpty(n.GetString()) ? n.GetString()! : id,
                Description: el.TryGetProperty("description", out var d) ? d.GetString() : null,
                ContextLength: el.TryGetProperty("context_length", out var c) && c.TryGetInt32(out var cl) ? cl : meta?.ContextLength,
                PromptPrice: el.TryGetProperty("pricing", out var pr) && pr.TryGetProperty("prompt", out var pp) && decimal.TryParse(pp.GetString(), out var p) ? p : meta?.PromptPrice,
                CompletionPrice: el.TryGetProperty("pricing", out var pc) && pc.TryGetProperty("completion", out var cp) && decimal.TryParse(cp.GetString(), out var cv) ? cv : meta?.CompletionPrice,
                // OpenRouter: architecture.input_modalities / supported_parameters.
                // Mistral: capabilities.function_calling / capabilities.vision.
                SupportsTools: ReadFlag(el, "supported_parameters", "tools") ?? ReadFlag(el, "capabilities", "function_calling") ?? meta?.SupportsTools,
                SupportsVision: ReadModality(el) ?? ReadFlag(el, "capabilities", "vision") ?? meta?.SupportsVision));
        }
        return list;
    }

    private static bool? ReadFlag(JsonElement el, string parent, string field)
    {
        if (el.TryGetProperty(parent, out var p) && p.ValueKind == JsonValueKind.Object &&
            p.TryGetProperty(field, out var f))
        {
            if (f.ValueKind == JsonValueKind.True) return true;
            if (f.ValueKind == JsonValueKind.False) return false;
            if (f.ValueKind == JsonValueKind.Array)
            {
                var found = false;
                foreach (var v in f.EnumerateArray())
                    if (v.GetString() == field || v.GetString() == "tools")
                        found = true;
                return found;
            }
        }
        return null;
    }

    private static bool? ReadModality(JsonElement el)
    {
        if (el.TryGetProperty("architecture", out var arch) && arch.ValueKind == JsonValueKind.Object &&
            arch.TryGetProperty("input_modalities", out var mods) && mods.ValueKind == JsonValueKind.Array)
        {
            var hasText = false;
            var hasImage = false;
            foreach (var m in mods.EnumerateArray())
            {
                if (m.GetString() == "text") hasText = true;
                if (m.GetString() == "image") hasImage = true;
            }
            return hasText && hasImage;
        }
        return null;
    }

    public async IAsyncEnumerable<ChatChunk> StreamChatAsync(
        ChatRequestPayload payload,
        ProviderCallContext? context = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var http = CreateClient(context);
        var body = BuildBody(payload);
        using var req = new HttpRequestMessage(HttpMethod.Post, "chat/completions") { Content = JsonContent.Create(body) };
        using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!res.IsSuccessStatusCode)
        {
            var text = await res.Content.ReadAsStringAsync(ct);
            yield return new ChatChunk.Error($"{_errorPrefix} {res.StatusCode}: {text}");
            yield break;
        }

        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var sr = new StreamReader(stream);

        int? promptTokens = null;
        int? completionTokens = null;
        // OpenAI streams tool calls as deltas keyed by index; accumulate id/name/args.
        var toolCalls = new Dictionary<int, (string? Id, string? Name, System.Text.StringBuilder Args)>();

        while (!sr.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await sr.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line)) continue;
            if (!line.StartsWith("data: ")) continue;
            var data = line["data: ".Length..];
            if (data == "[DONE]")
            {
                foreach (var tc in FlushToolCalls(toolCalls))
                    yield return tc;
                yield return new ChatChunk.Usage(promptTokens, completionTokens);
                yield return new ChatChunk.Done();
                yield break;
            }

            using var chunk = JsonDocument.Parse(data);
            var root = chunk.RootElement;

            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                var delta = choice.TryGetProperty("delta", out var d) ? d : default;
                if (delta.ValueKind == JsonValueKind.Object)
                {
                    if (delta.TryGetProperty("content", out var c) &&
                        c.ValueKind == JsonValueKind.String)
                    {
                        var text = c.GetString();
                        if (!string.IsNullOrEmpty(text))
                            yield return new ChatChunk.Token(text);
                    }

                    if (delta.TryGetProperty("tool_calls", out var tcs) && tcs.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var tc in tcs.EnumerateArray())
                        {
                            var index = tc.TryGetProperty("index", out var ix) && ix.TryGetInt32(out var ixv) ? ixv : 0;
                            var fn = tc.TryGetProperty("function", out var f) ? f : default;
                            var existing = toolCalls.TryGetValue(index, out var acc) ? acc : (null, null, new System.Text.StringBuilder());
                            if (tc.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                                existing.Id = idEl.GetString();
                            if (fn.ValueKind == JsonValueKind.Object)
                            {
                                if (fn.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(nm.GetString()))
                                    existing.Name = nm.GetString();
                                if (fn.TryGetProperty("arguments", out var ar) && ar.ValueKind == JsonValueKind.String)
                                    existing.Args.Append(ar.GetString());
                            }
                            toolCalls[index] = existing;
                        }
                    }
                }

                // finish_reason "tool_calls" marks the end of the tool-call sequence.
                if (choice.TryGetProperty("finish_reason", out var fr) && fr.GetString() == "tool_calls")
                {
                    foreach (var tc in FlushToolCalls(toolCalls))
                        yield return tc;
                }
            }

            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("prompt_tokens", out var pt) && pt.TryGetInt32(out var ptv)) promptTokens = ptv;
                if (usage.TryGetProperty("completion_tokens", out var cpt) && cpt.TryGetInt32(out var cptv)) completionTokens = cptv;
            }
        }

        foreach (var tc in FlushToolCalls(toolCalls))
            yield return tc;
        yield return new ChatChunk.Usage(promptTokens, completionTokens);
        yield return new ChatChunk.Done();
    }

    private static List<ChatChunk.ToolCall> FlushToolCalls(Dictionary<int, (string? Id, string? Name, System.Text.StringBuilder Args)> acc)
    {
        var list = new List<ChatChunk.ToolCall>();
        foreach (var (_, (id, name, args)) in acc.OrderBy(k => k.Key))
            list.Add(new ChatChunk.ToolCall(id ?? $"call_{list.Count}", name ?? "", args.ToString()));
        acc.Clear();
        return list;
    }

    private Dictionary<string, object?> BuildBody(ChatRequestPayload payload)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = payload.Model,
            ["messages"] = payload.Messages.Select(SerializeMessage).ToList(),
            ["stream"] = true,
            ["stream_options"] = new Dictionary<string, object?> { ["include_usage"] = true },
            ["temperature"] = payload.Temperature,
            ["max_tokens"] = payload.MaxTokens
        };
        if (payload.Tools is { Count: > 0 })
        {
            body["tools"] = payload.Tools.Select(t => new Dictionary<string, object?>
            {
                ["type"] = "function",
                ["function"] = new Dictionary<string, object?>
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["parameters"] = ParseJson(t.ParametersJson)
                }
            }).ToList();
        }
        return body;
    }

    private static object SerializeMessage(ChatMessagePayload m)
    {
        if (m.Role == MessageRole.Tool)
        {
            return new Dictionary<string, object?>
            {
                ["role"] = "tool",
                ["tool_call_id"] = m.ToolCallId,
                ["content"] = m.Content
            };
        }
        if (m.Role == MessageRole.Assistant && m.ToolCalls is { Count: > 0 })
        {
            return new Dictionary<string, object?>
            {
                ["role"] = "assistant",
                ["content"] = m.Content,
                ["tool_calls"] = m.ToolCalls.Select(tc => new Dictionary<string, object?>
                {
                    ["id"] = tc.Id,
                    ["type"] = "function",
                    ["function"] = new Dictionary<string, object?>
                    {
                        ["name"] = tc.Name,
                        ["arguments"] = tc.ArgumentsJson
                    }
                }).ToList()
            };
        }
        return new Dictionary<string, object?>
        {
            ["role"] = RoleToString(m.Role),
            ["content"] = m.Content
        };
    }

    protected static JsonElement? ParseJson(string? json)
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

    private static string RoleToString(MessageRole r) => r switch
    {
        MessageRole.System => "system",
        MessageRole.User => "user",
        MessageRole.Assistant => "assistant",
        MessageRole.Tool => "tool",
        _ => "user"
    };
}
