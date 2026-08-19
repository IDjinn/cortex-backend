using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Cortex.Core.Auth;
using Cortex.Core.Objects;

namespace Cortex.Core.Providers;

/// <summary>
/// OpenAI-compatible provider: chat/completions over SSE ("data:" lines with a
/// "[DONE]" sentinel) and a "models" listing endpoint. Shared by OpenRouter and
/// LM Studio; subclasses set base URL, auth and provider-specific headers.
/// </summary>
public abstract class OpenAiCompatibleProvider : IProvider
{
    protected HttpClient Http { get; }
    private readonly string _errorPrefix;

    protected OpenAiCompatibleProvider(
        IHttpClientFactory factory,
        string httpClientName,
        ProviderOptions.ProviderEndpoint endpoint,
        string errorPrefix)
    {
        Http = factory.CreateClient(httpClientName);
        Http.BaseAddress = new Uri(endpoint.BaseUrl.TrimEnd('/') + "/");
        if (!string.IsNullOrEmpty(endpoint.ApiKey))
            Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.ApiKey);
        _errorPrefix = errorPrefix;
    }

    public abstract ChatProviderKind Kind { get; }

    public virtual async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken ct = default)
    {
        var res = await Http.GetAsync("models", ct);
        res.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var list = new List<ModelInfo>();
        foreach (var el in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            var id = el.GetProperty("id").GetString()!;
            list.Add(new ModelInfo(
                Id: id,
                Name: el.TryGetProperty("name", out var n) && !string.IsNullOrEmpty(n.GetString()) ? n.GetString()! : id,
                Description: el.TryGetProperty("description", out var d) ? d.GetString() : null,
                ContextLength: el.TryGetProperty("context_length", out var c) && c.TryGetInt32(out var cl) ? cl : null,
                PromptPrice: el.TryGetProperty("pricing", out var pr) && pr.TryGetProperty("prompt", out var pp) && decimal.TryParse(pp.GetString(), out var p) ? p : null,
                CompletionPrice: el.TryGetProperty("pricing", out var pc) && pc.TryGetProperty("completion", out var cp) && decimal.TryParse(cp.GetString(), out var cv) ? cv : null
            ));
        }
        return list;
    }

    public async IAsyncEnumerable<ChatChunk> StreamChatAsync(
        ChatRequestPayload payload,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var body = new
        {
            model = payload.Model,
            messages = payload.Messages.Select(m => new { role = RoleToString(m.Role), content = m.Content }),
            stream = true,
            stream_options = new { include_usage = true },
            temperature = payload.Temperature,
            max_tokens = payload.MaxTokens
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(body)
        };
        using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
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

        while (!sr.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await sr.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line)) continue;
            if (!line.StartsWith("data: ")) continue;
            var data = line["data: ".Length..];
            if (data == "[DONE]")
            {
                yield return new ChatChunk.Usage(promptTokens, completionTokens);
                yield return new ChatChunk.Done();
                yield break;
            }

            using var chunk = JsonDocument.Parse(data);
            var root = chunk.RootElement;

            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var delta = choices[0].TryGetProperty("delta", out var d) ? d : default;
                if (delta.ValueKind == JsonValueKind.Object &&
                    delta.TryGetProperty("content", out var c) &&
                    c.ValueKind == JsonValueKind.String)
                {
                    var text = c.GetString();
                    if (!string.IsNullOrEmpty(text))
                        yield return new ChatChunk.Token(text);
                }
            }

            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("prompt_tokens", out var pt) && pt.TryGetInt32(out var ptv)) promptTokens = ptv;
                if (usage.TryGetProperty("completion_tokens", out var cpt) && cpt.TryGetInt32(out var cptv)) completionTokens = cptv;
            }
        }

        yield return new ChatChunk.Usage(promptTokens, completionTokens);
        yield return new ChatChunk.Done();
    }

    private static string RoleToString(MessageRole r) => r switch
    {
        MessageRole.System => "system",
        MessageRole.User => "user",
        MessageRole.Assistant => "assistant",
        _ => "user"
    };
}
