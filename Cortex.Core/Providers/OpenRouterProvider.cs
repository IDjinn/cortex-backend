using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Cortex.Core.Auth;
using Cortex.Core.Objects;
using Microsoft.Extensions.Options;

namespace Cortex.Core.Providers;

public sealed class OpenRouterProvider : IProvider
{
    public const string HttpClientName = "openrouter";

    private readonly HttpClient _http;
    private readonly ProviderOptions _opts;

    public OpenRouterProvider(IHttpClientFactory factory, IOptions<ProviderOptions> opts)
    {
        _http = factory.CreateClient(HttpClientName);
        _opts = opts.Value;
        _http.BaseAddress = new Uri(_opts.OpenRouter.BaseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _opts.OpenRouter.ApiKey);
        _http.DefaultRequestHeaders.Add("HTTP-Referer", "https://cortex.app");
        _http.DefaultRequestHeaders.Add("X-Title", "Cortex");
    }

    public ChatProviderKind Kind => ChatProviderKind.OpenRouter;

    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken ct = default)
    {
        var res = await _http.GetAsync("models", ct);
        res.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var list = new List<ModelInfo>();
        foreach (var el in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            list.Add(new ModelInfo(
                Id: el.GetProperty("id").GetString()!,
                Name: el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : el.GetProperty("id").GetString()!,
                Description: el.TryGetProperty("description", out var d) ? d.GetString() : null,
                ContextLength: el.TryGetProperty("context_length", out var c) && c.TryGetInt32(out var cl) ? cl : null,
                PromptPrice: el.TryGetProperty("pricing", out var pr) && pr.TryGetProperty("prompt", out var pp) && decimal.TryParse(pp.GetString(), out var p) ? p : null,
                CompletionPrice: el.TryGetProperty("pricing", out var pc) && pc.TryGetProperty("completion", out var cp) && decimal.TryParse(cp.GetString(), out var cv) ? cv : null
            ));
        }
        return list;
    }

    public async IAsyncEnumerable<ChatChunk> StreamChatAsync(ChatRequestPayload payload, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var body = new
        {
            model = payload.Model,
            messages = payload.Messages.Select(m => new { role = RoleToString(m.Role), content = m.Content }),
            stream = true,
            temperature = payload.Temperature,
            max_tokens = payload.MaxTokens
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(body)
        };
        using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!res.IsSuccessStatusCode)
        {
            var text = await res.Content.ReadAsStringAsync(ct);
            yield return new ChatChunk.Error($"OpenRouter {res.StatusCode}: {text}");
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
