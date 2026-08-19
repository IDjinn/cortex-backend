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
    public sealed record Notice(string Message) : ChatTurnEvent;
    public sealed record Completed(int? TokensIn, int? TokensOut, string Provider, string Model, decimal? CostUsd) : ChatTurnEvent;
    public sealed record Failed(string Message) : ChatTurnEvent;
    /// <summary>Post-turn automatic extraction: candidate memories the user must confirm.</summary>
    public sealed record MemoryProposals(IReadOnlyList<string> Proposals) : ChatTurnEvent;
}

public class ChatService : IChatService
{
    private readonly AppDbContext _db;
    private readonly IConversationService _conversations;
    private readonly IProviderFactory _providers;
    private readonly IProviderKeyStore _keyStore;
    private readonly IModelService _models;
    private readonly IMemoryService _memories;
    private readonly ProviderOptions _providerOptions;
    private readonly ChatOptions _chatOptions;

    public ChatService(
        AppDbContext db,
        IConversationService conversations,
        IProviderFactory providers,
        IProviderKeyStore keyStore,
        IModelService models,
        IMemoryService memories,
        IOptions<ProviderOptions> providerOptions,
        IOptions<ChatOptions> chatOptions)
    {
        _db = db;
        _conversations = conversations;
        _providers = providers;
        _keyStore = keyStore;
        _models = models;
        _memories = memories;
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

        // Memory injection (relevance budget: top-K, max chars) — second system
        // payload, also outside the history window.
        var memories = await _memories.RelevantAsync(userId, conv.Id, _chatOptions.MemoryTopK, _chatOptions.MemoryMaxPromptChars, ct);
        if (memories.Count > 0)
        {
            var block = "Known memories about the user (background context — do not mention explicitly unless relevant):\n"
                + string.Join("\n", memories.Select(m => $"- {m.Content}"));
            history.Insert(1, new ChatMessagePayload(MessageRole.System, block));
        }

        // 4. stream — primary first, then the manual fallback when the primary
        // fails before emitting any token (mid-stream failures surface as errors).
        var attempts = new List<(ChatProviderKind Provider, string Model)> { (conv.Provider, model) };
        if (TryParseFallback(conv, out var fallback))
            attempts.Add(fallback);

        var buffer = new System.Text.StringBuilder();
        int? tokensIn = null;
        int? tokensOut = null;
        string? error = null;
        var servedProvider = conv.Provider;
        var servedModel = model;
        // The request-header BYOK key belongs to the conversation's primary provider —
        // never reused for a fallback provider of a different kind.
        var headerKey = context?.ApiKey;

        for (var i = 0; i < attempts.Count; i++)
        {
            var (attemptProvider, attemptModel) = attempts[i];
            if (i > 0)
                yield return new ChatTurnEvent.Notice($"Provedor principal indisponível — usando reserva {attemptModel}.");

            var attempt = _providers.Get(attemptProvider);
            // BYOK resolution per attempt: request-header key (primary only) > user vault > server-configured key.
            string? attemptKey = attemptProvider == conv.Provider ? headerKey : null;
            if (string.IsNullOrWhiteSpace(attemptKey))
            {
                var vaultKey = await _keyStore.GetKeyAsync(userId, attemptProvider, ct);
                if (!string.IsNullOrEmpty(vaultKey)) attemptKey = vaultKey;
            }
            var attemptContext = new ProviderCallContext(attemptKey, context?.BaseUrl);

            buffer.Clear();
            error = null;
            tokensIn = null;
            tokensOut = null;
            var gotOutput = false;
            servedProvider = attemptProvider;
            servedModel = attemptModel;

            await foreach (var chunk in attempt.StreamChatAsync(new ChatRequestPayload(attemptModel, history), attemptContext, ct))
            {
                switch (chunk)
                {
                    case ChatChunk.Token t:
                        gotOutput = true;
                        buffer.Append(t.Text);
                        yield return new ChatTurnEvent.Token(t.Text);
                        break;
                    case ChatChunk.ToolCall tc:
                        gotOutput = true;
                        yield return new ChatTurnEvent.ToolCallChunk(tc.Id, tc.Name, tc.ArgumentsJson);
                        break;
                    case ChatChunk.Usage u:
                        tokensIn = u.PromptTokens;
                        tokensOut = u.CompletionTokens;
                        break;
                    case ChatChunk.Error e:
                        error = e.Message;
                        break;
                    case ChatChunk.Done:
                        break;
                }
                if (error is not null)
                    break;
            }

            if (error is null || gotOutput)
                break; // success, or a mid-stream failure we must surface honestly
        }

        if (error is not null)
            yield return new ChatTurnEvent.Failed(error);

        // 5. finalize the assistant message content (model reflects who served the turn)
        var cost = await ComputeCostAsync(servedProvider, servedModel, tokensIn, tokensOut, ct);
        var toSave = await _db.Messages.FindAsync(new object?[] { assistantMsg.Id }, ct);
        if (toSave is not null)
        {
            toSave.Content = buffer.ToString();
            toSave.TokensIn = tokensIn;
            toSave.TokensOut = tokensOut;
            toSave.Cost = cost;
            toSave.Model = servedModel;
            toSave.Error = error;
            await _db.SaveChangesAsync(ct);
        }

        yield return new ChatTurnEvent.Completed(tokensIn, tokensOut, servedProvider.ToString(), servedModel, cost);

        // 6. automatic memory extraction — one extra call per turn, only on a
        // successful turn of an already-established conversation (>= 4 messages).
        // Proposals are surfaced to the client for user confirmation.
        if (error is null && conv.Messages.Count >= 4)
        {
            var proposals = await ExtractMemoryProposalsAsync(userId, conv.Id, servedProvider, servedModel, history, ct);
            if (proposals.Count > 0)
                yield return new ChatTurnEvent.MemoryProposals(proposals);
        }
    }

    /// <summary>Asks the conversation's model to propose durable facts from the recent exchange.</summary>
    private async Task<IReadOnlyList<string>> ExtractMemoryProposalsAsync(
        Guid userId, Guid conversationId, ChatProviderKind provider, string model, List<ChatMessagePayload> history, CancellationToken ct)
    {
        try
        {
            var key = await _keyStore.GetKeyAsync(userId, provider, ct);
            var context = new ProviderCallContext(key);

            const string prompt = """
                Analyze the conversation above and extract at most 3 durable facts worth remembering about the user for future conversations (preferences, ongoing projects, important constraints).
                Respond with ONLY a JSON array of short strings (max 140 chars each), e.g. ["prefers TypeScript", "building a mobile app"].
                If there is nothing worth remembering, respond with [].
                """;
            var payload = new List<ChatMessagePayload>(history.Count + 1);
            payload.AddRange(history.TakeLast(10));
            payload.Add(new ChatMessagePayload(MessageRole.User, prompt));

            var sb = new System.Text.StringBuilder();
            await foreach (var chunk in _providers.Get(provider).StreamChatAsync(new ChatRequestPayload(model, payload), context, ct))
            {
                if (chunk is ChatChunk.Token t) sb.Append(t.Text);
                else if (chunk is ChatChunk.Error) return Array.Empty<string>();
            }
            return ParseProposals(sb.ToString());
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<string> ParseProposals(string raw)
    {
        var text = raw.Trim();
        // tolerate markdown fences around the JSON
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        if (start < 0 || end <= start) return Array.Empty<string>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(text[start..(end + 1)]);
            return doc.RootElement.EnumerateArray()
                .Select(e => e.GetString())
                .Where(s => !string.IsNullOrWhiteSpace(s) && s.Length <= 400)
                .Select(s => s!.Trim())
                .Take(3)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static bool TryParseFallback(Conversation conv, out (ChatProviderKind Provider, string Model) fallback)
    {
        fallback = default;
        return !string.IsNullOrWhiteSpace(conv.FallbackProvider)
            && !string.IsNullOrWhiteSpace(conv.FallbackModel)
            && Enum.TryParse<ChatProviderKind>(conv.FallbackProvider, ignoreCase: true, out var provider)
            && (fallback = (provider, conv.FallbackModel!)) != default;
    }

    /// <summary>USD cost of a turn from the model's cached price; null for local/free models.</summary>
    private async Task<decimal?> ComputeCostAsync(ChatProviderKind provider, string model, int? tokensIn, int? tokensOut, CancellationToken ct)
    {
        if (tokensIn is null && tokensOut is null) return null;
        try
        {
            var models = await _models.ListAsync(provider, ct: ct);
            var info = models.FirstOrDefault(m => m.Id.Equals(model, StringComparison.OrdinalIgnoreCase));
            if (info?.PromptPrice is null && info?.CompletionPrice is null) return null;
            return tokensIn / 1_000_000m * (info?.PromptPrice ?? 0m)
                 + tokensOut / 1_000_000m * (info?.CompletionPrice ?? 0m);
        }
        catch
        {
            return null;
        }
    }
}
