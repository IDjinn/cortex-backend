using System.Text.Json;
using Cortex.Core.Auth;
using Cortex.Core.Dtos;
using Cortex.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cortex.Core.Controllers;

[ApiController]
[Authorize]
[Route("api/chat")]
public class ChatController : ControllerBase
{
    private readonly ICurrentUser _me;
    private readonly IChatService _chat;

    public ChatController(ICurrentUser me, IChatService chat)
    {
        _me = me;
        _chat = chat;
    }

    /// <summary>
    /// Sends a user message and streams completion tokens as Server-Sent Events.
    /// </summary>
    [HttpPost]
    public async Task Stream([FromBody] ChatRequest req, CancellationToken ct)
    {
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

        try
        {
            await foreach (var ev in _chat.StreamTurnAsync(_me.UserId, req.ConversationId, req.Content, ct))
            {
                switch (ev)
                {
                    case ChatTurnEvent.UserMessageSaved u:
                        await SendEvent("user", new { messageId = u.MessageId });
                        break;
                    case ChatTurnEvent.AssistantMessageCreated a:
                        await SendEvent("assistant", new { messageId = a.MessageId });
                        break;
                    case ChatTurnEvent.Token t:
                        await SendEvent("token", new { value = t.Text });
                        break;
                    case ChatTurnEvent.Completed c:
                        await SendEvent("done", new { tokensIn = c.TokensIn, tokensOut = c.TokensOut });
                        break;
                    case ChatTurnEvent.Failed f:
                        await SendEvent("error", new { message = f.Message });
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // client disconnected - just stop
        }
        catch (Exception ex)
        {
            await SendEvent("error", new { message = ex.Message });
        }
    }
}
