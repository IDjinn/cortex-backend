using System.Text.Json;
using Cortex.Core.Auth;
using Cortex.Core.Dtos;
using Cortex.Core.Objects;
using Cortex.Core.Providers;
using Cortex.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Cortex.Core.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/chat/anonymous")]
public class AnonymousChatController : ControllerBase
{
    private const string ProviderKeyHeader = "X-Provider-Key";
    private const int RequestsPerMinute = 30;

    private readonly IProviderFactory _factory;
    private readonly ProviderOptions _providers;
    private readonly ChatOptions _chat;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AnonymousChatController> _log;

    public AnonymousChatController(
        IProviderFactory factory,
        IOptions<ProviderOptions> providers,
        IOptions<ChatOptions> chat,
        IMemoryCache cache,
        ILogger<AnonymousChatController> log)
    {
        _factory = factory;
        _providers = providers.Value;
        _chat = chat.Value;
        _cache = cache;
        _log = log;
    }

    /// <summary>
    /// Streams completion tokens as SSE without requiring authentication.
    /// Conversations are not persisted — caller keeps the history. Local providers
    /// are always allowed; remote (cloud) providers require the caller's own API
    /// key, proxied per request via header and never stored.
    /// </summary>
    [HttpPost]
    public async Task Stream([FromBody] AnonymousChatRequest req, CancellationToken ct)
    {
        if (req.Messages is null || req.Messages.Count == 0)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new ErrorDetail("Messages cannot be empty"), ct);
            return;
        }

        var providerKey = Request.Headers.TryGetValue(ProviderKeyHeader, out var key) ? key.ToString() : null;
        if (string.IsNullOrWhiteSpace(providerKey)) providerKey = null;

        var isLocal = req.Provider is (ChatProviderKind.Ollama or ChatProviderKind.LmStudio);
        if (!isLocal && providerKey is null)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            await Response.WriteAsJsonAsync(
                new ErrorDetail("Guest chat needs a provider API key for cloud models", "Send your own key via the X-Provider-Key header, or use a local provider."), ct);
            return;
        }

        // Custom base URLs are only meaningful for local endpoints.
        if (!string.IsNullOrWhiteSpace(req.BaseUrl) && !isLocal)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new ErrorDetail("baseUrl is only allowed for local providers"), ct);
            return;
        }

        if (IsRateLimited())
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await Response.WriteAsJsonAsync(new ErrorDetail("Too many anonymous requests", "Try again in a minute."), ct);
            return;
        }

        var model = req.Model;
        if (string.IsNullOrWhiteSpace(model))
        {
            model = _providers.For(req.Provider).DefaultModel;
            if (string.IsNullOrWhiteSpace(model))
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                await Response.WriteAsJsonAsync(new ErrorDetail("Model is required"), ct);
                return;
            }
        }

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        await using var writer = new StreamWriter(Response.Body);
        // No AutoFlush: its setter Flush()es synchronously, which Kestrel
        // (AllowSynchronousIO=false) rejects. SendEvent FlushAsync()s per event.

        async Task SendEvent(string type, object? data)
        {
            var json = data is null ? "null" : JsonSerializer.Serialize(data);
            await writer.WriteAsync($"event: {type}\n");
            await writer.WriteAsync($"data: {json}\n\n");
            await writer.FlushAsync(ct);
        }

        var provider = _factory.Get(req.Provider);
        var messages = req.Messages.Select(m => new ChatMessagePayload(m.Role, m.Content)).ToList();
        var instructions = ChatInstructions.Build(req.Locale, _chat.SystemPromptTemplate);
        if (!string.IsNullOrWhiteSpace(instructions))
            messages.Insert(0, new ChatMessagePayload(MessageRole.System, instructions));
        var payload = new ChatRequestPayload(model, messages, req.Temperature, req.MaxTokens);
        var context = new ProviderCallContext(providerKey, req.BaseUrl);

        try
        {
            await foreach (var chunk in provider.StreamChatAsync(payload, context, ct))
            {
                switch (chunk)
                {
                    case ChatChunk.Token t:
                        await SendEvent("token", new { value = t.Text });
                        break;
                    case ChatChunk.ToolCall tc:
                        await SendEvent("toolCall", new { id = tc.Id, name = tc.Name, arguments = tc.ArgumentsJson });
                        break;
                    case ChatChunk.Usage u:
                        await SendEvent("usage", new { tokensIn = u.PromptTokens, tokensOut = u.CompletionTokens });
                        break;
                    case ChatChunk.Done:
                        await SendEvent("done", null);
                        break;
                    case ChatChunk.Error e:
                        // Provider returned an error — most likely 402 (paid model) or 429 (rate).
                        Response.StatusCode = StatusCodes.Status200OK; // keep stream open, send error event
                        await SendEvent("error", new { message = e.Message });
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // client disconnected
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Anonymous chat failed");
            await SendEvent("error", new { message = ex.Message });
        }
    }

    /// <summary>Fixed-window per-IP limiter — the anonymous endpoint proxies third-party APIs.</summary>
    private bool IsRateLimited()
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var window = DateTimeOffset.UtcNow.Ticks / TimeSpan.TicksPerMinute;
        var key = $"anon-rl:{ip}:{window}";
        _cache.TryGetValue(key, out int count);
        _cache.Set(key, count + 1, TimeSpan.FromSeconds(70));
        return count + 1 > RequestsPerMinute;
    }
}
