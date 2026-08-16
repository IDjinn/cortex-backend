using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Cortex.Core.Auth;
using Cortex.Core.Objects;
using Microsoft.Extensions.Options;

namespace Cortex.Core.Providers;

public sealed class OllamaProvider : IProvider
{
    public const string HttpClientName = "ollama";

    private readonly HttpClient _http;

    public OllamaProvider(IHttpClientFactory factory, IOptions<ProviderOptions> opts)
    {
        _http = factory.CreateClient(HttpClientName);
        _http.BaseAddress = new Uri(opts.Value.Ollama.BaseUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromMinutes(10);
    }

    public ChatProviderKind Kind => ChatProviderKind.Ollama;

    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken ct = default)
    {
        var res = await _http.GetAsync("api/tags", ct);
        res.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var list = new List<ModelInfo>();
        if (doc.RootElement.TryGetProperty("models", out var models))
        {
            foreach (var m in models.EnumerateArray())
            {
                var id = m.GetProperty("name").GetString()!;
                list.Add(new ModelInfo(
                    Id: id,
                    Name: id,
                    Description: m.TryGetProperty("details", out var d) && d.TryGetProperty("parameter_size", out var ps) ? ps.GetString() : null,
                    ContextLength: null,
                    PromptPrice: null,
                    CompletionPrice: null));
            }
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
            options = new
            {
                temperature = payload.Temperature,
                num_predict = payload.MaxTokens
            }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "api/chat")
        {
            Content = JsonContent.Create(body)
        };
        using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!res.IsSuccessStatusCode)
        {
            var text = await res.Content.ReadAsStringAsync(ct);
            yield return new ChatChunk.Error($"Ollama {res.StatusCode}: {text}");
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

            using var chunk = JsonDocument.Parse(line);
            var root = chunk.RootElement;

            if (root.TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("content", out var c) &&
                c.ValueKind == JsonValueKind.String)
            {
                var text = c.GetString();
                if (!string.IsNullOrEmpty(text))
                    yield return new ChatChunk.Token(text);
            }

            if (root.TryGetProperty("prompt_eval_count", out var pec) && pec.TryGetInt32(out var pecv)) promptTokens = pecv;
            if (root.TryGetProperty("eval_count", out var ec) && ec.TryGetInt32(out var ecv)) completionTokens = ecv;

            if (root.TryGetProperty("done", out var done) && done.GetBoolean())
            {
                yield return new ChatChunk.Usage(promptTokens, completionTokens);
                yield return new ChatChunk.Done();
                yield break;
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
