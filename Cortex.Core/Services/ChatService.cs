using System.Runtime.CompilerServices;
using Cortex.Core.Auth;
using Cortex.Core.Data;
using Cortex.Core.Objects;
using Cortex.Core.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cortex.Core.Services;

public interface IChatService
{
    /// <summary>
    /// Orchestrates a turn: persists the user message, builds the prompt, streams tokens
    /// from the provider, persists the assistant message at the end.
    /// </summary>
    IAsyncEnumerable<ChatTurnEvent> StreamTurnAsync(
        Guid userId,
        Guid conversationId,
        string userContent,
        string? locale = null,
        ProviderCallContext? context = null,
        CancellationToken ct = default);
}

public abstract record ChatTurnEvent
{
    public sealed record UserMessageSaved(Guid MessageId) : ChatTurnEvent;
    public sealed record AssistantMessageCreated(Guid MessageId) : ChatTurnEvent;
    public sealed record Token(string Text) : ChatTurnEvent;
    public sealed record ToolCallChunk(string Id, string Name, string ArgumentsJson) : ChatTurnEvent;
    public sealed record Completed(int? TokensIn, int? TokensOut) : ChatTurnEvent;
    public sealed record Failed(string Message) : ChatTurnEvent;
}

public class ChatService : IChatService
{
    private readonly AppDbContext _db;
    private readonly IConversationService _conversations;
    private readonly IProviderFactory _providers;
    private readonly IProviderKeyStore _keyStore;
    private readonly ProviderOptions _providerOptions;
    private readonly ChatOptions _chatOptions;

    public ChatService(
        AppDbContext db,
        IConversationService conversations,
        IProviderFactory providers,
        IProviderKeyStore keyStore,
        IOptions<ProviderOptions> providerOptions,
        IOptions<ChatOptions> chatOptions)
    {
        _db = db;
        _conversations = conversations;
        _providers = providers;
        _keyStore = keyStore;
        _providerOptions = providerOptions.Value;
        _chatOptions = chatOptions.Value;
    }

    public async IAsyncEnumerable<ChatTurnEvent> StreamTurnAsync(
        Guid userId,
        Guid conversationId,
        string userContent,
        string? locale = null,
        ProviderCallContext? context = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        Conversation? conv;
        Exception? loadError = null;
        try
        {
            conv = await _db.Conversations
                .Include(c => c.Messages.OrderBy(m => m.CreatedAt))
                .FirstOrDefaultAsync(c => c.UserId == userId && c.Id == conversationId, ct);
        }
        catch (Exception ex)
        {
            loadError = ex;
            conv = null;
        }

        if (loadError is not null)
        {
            yield return new ChatTurnEvent.Failed(loadError.Message);
            yield break;
        }

        if (conv is null)
        {
            yield return new ChatTurnEvent.Failed("Conversation not found");
            yield break;
        }

        // Conversations created before a default was configured may carry an empty model.
        var model = string.IsNullOrWhiteSpace(conv.Model)
            ? _providerOptions.For(conv.Provider).DefaultModel ?? conv.Model
            : conv.Model;

        // 1. persist user message
        var userMsg = await _conversations.AppendMessageAsync(conv.Id, MessageRole.User, userContent, null, ct);
        yield return new ChatTurnEvent.UserMessageSaved(userMsg.Id);

        // 2. create empty assistant message placeholder
        var assistantMsg = await _conversations.AppendMessageAsync(conv.Id, MessageRole.Assistant, "", model, ct);
        yield return new ChatTurnEvent.AssistantMessageCreated(assistantMsg.Id);

        // 3. build prompt
        var history = conv.Messages
            .Where(m => m.Id != userMsg.Id && m.Id != assistantMsg.Id)
            .TakeLast(50)
            .Select(m => new ChatMessagePayload(m.Role, m.Content))
            .ToList();
        history.Add(new ChatMessagePayload(MessageRole.User, userContent));

        // Base instructions (language hint etc.) always lead the prompt, outside
        // the 50-message window so they are never truncated away.
        var instructions = ChatInstructions.Build(locale, _chatOptions.SystemPromptTemplate);
        if (!string.IsNullOrWhiteSpace(instructions))
            history.Insert(0, new ChatMessagePayload(MessageRole.System, instructions));

        var payload = new ChatRequestPayload(model, history);
        var provider = _providers.Get(conv.Provider);

        // BYOK resolution: request-header key > user vault > server-configured key.
        if (string.IsNullOrWhiteSpace(context?.ApiKey))
        {
            var vaultKey = await _keyStore.GetKeyAsync(userId, conv.Provider, ct);
            if (!string.IsNullOrEmpty(vaultKey))
                context = new ProviderCallContext(vaultKey, context?.BaseUrl);
        }

        var buffer = new System.Text.StringBuilder();
        int? tokensIn = null;
        int? tokensOut = null;
        string? error = null;

        // 4. stream
        await foreach (var chunk in provider.StreamChatAsync(payload, context, ct))
        {
            switch (chunk)
            {
                case ChatChunk.Token t:
                    buffer.Append(t.Text);
                    yield return new ChatTurnEvent.Token(t.Text);
                    break;
                case ChatChunk.ToolCall tc:
                    yield return new ChatTurnEvent.ToolCallChunk(tc.Id, tc.Name, tc.ArgumentsJson);
                    break;
                case ChatChunk.Usage u:
                    tokensIn = u.PromptTokens;
                    tokensOut = u.CompletionTokens;
                    break;
                case ChatChunk.Error e:
                    error = e.Message;
                    yield return new ChatTurnEvent.Failed(error);
                    break;
                case ChatChunk.Done:
                    break;
            }
        }

        // 5. finalize the assistant message content
        var toSave = await _db.Messages.FindAsync(new object?[] { assistantMsg.Id }, ct);
        if (toSave is not null)
        {
            toSave.Content = buffer.ToString();
            toSave.TokensIn = tokensIn;
            toSave.TokensOut = tokensOut;
            toSave.Error = error;
            await _db.SaveChangesAsync(ct);
        }

        yield return new ChatTurnEvent.Completed(tokensIn, tokensOut);
    }
}
