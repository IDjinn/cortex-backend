using System.Text.Json;
using Cortex.Core.Dtos;
using Cortex.Core.Objects;
using Cortex.Core.Providers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cortex.Core.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/chat/anonymous")]
public class AnonymousChatController : ControllerBase
{
    private readonly IProviderFactory _factory;
    private readonly ILogger<AnonymousChatController> _log;

    public AnonymousChatController(IProviderFactory factory, ILogger<AnonymousChatController> log)
    {
        _factory = factory;
        _log = log;
    }

    /// <summary>
    /// Streams completion tokens as SSE without requiring authentication.
    /// Conversations are not persisted — caller is responsible for keeping history.
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

        if (string.IsNullOrWhiteSpace(req.Model))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new ErrorDetail("Model is required"), ct);
            return;
        }

        // Guest (anonymous) chat is restricted to local Ollama models.
        // Cloud providers (e.g. OpenRouter) require an authenticated account.
        if (req.Provider != ChatProviderKind.Ollama)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            await Response.WriteAsJsonAsync(
                new ErrorDetail("Guest chat is restricted to Ollama", "Sign in to use cloud models."), ct);
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        await using var writer = new StreamWriter(Response.Body);
        writer.AutoFlush = true;

        async Task SendEvent(string type, object? data)
        {
            var json = data is null ? "null" : JsonSerializer.Serialize(data);
            await writer.WriteAsync($"event: {type}\n");
            await writer.WriteAsync($"data: {json}\n\n");
            await writer.FlushAsync(ct);
        }

        var provider = _factory.Get(req.Provider);
        var payload = new ChatRequestPayload(
            req.Model,
            req.Messages.Select(m => new ChatMessagePayload(m.Role, m.Content)).ToList(),
            req.Temperature,
            req.MaxTokens);

        try
        {
            await foreach (var chunk in provider.StreamChatAsync(payload, ct))
            {
                switch (chunk)
                {
                    case ChatChunk.Token t:
                        await SendEvent("token", new { value = t.Text });
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
}
