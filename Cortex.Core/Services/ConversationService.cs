using Cortex.Core.Auth;
using Cortex.Core.Data;
using Cortex.Core.Objects;
using Microsoft.EntityFrameworkCore;

namespace Cortex.Core.Services;

public interface IConversationService
{
    Task<List<Conversation>> ListAsync(Guid userId, CancellationToken ct = default);
    Task<Conversation?> GetAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<Conversation> CreateAsync(Guid userId, string? title, ChatProviderKind provider, string model, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid userId, Guid id, string? title, bool? pinned, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<Message> AppendMessageAsync(Guid conversationId, MessageRole role, string content, string? model, CancellationToken ct = default);
    Task FinalizeAssistantMessageAsync(Guid messageId, int? tokensIn, int? tokensOut, string? error, CancellationToken ct = default);
}

public class ConversationService : IConversationService
{
    private readonly AppDbContext _db;

    public ConversationService(AppDbContext db) => _db = db;

    public Task<List<Conversation>> ListAsync(Guid userId, CancellationToken ct = default) =>
        _db.Conversations
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.Pinned)
            .ThenByDescending(c => c.UpdatedAt)
            .ToListAsync(ct);

    public Task<Conversation?> GetAsync(Guid userId, Guid id, CancellationToken ct = default) =>
        _db.Conversations
            .Include(c => c.Messages.OrderBy(m => m.CreatedAt))
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Id == id, ct);

    public async Task<Conversation> CreateAsync(Guid userId, string? title, ChatProviderKind provider, string model, CancellationToken ct = default)
    {
        var conv = new Conversation
        {
            UserId = userId,
            Title = string.IsNullOrWhiteSpace(title) ? "Nova conversa" : title,
            Provider = provider,
            Model = model
        };
        _db.Conversations.Add(conv);
        await _db.SaveChangesAsync(ct);
        return conv;
    }

    public async Task<bool> UpdateAsync(Guid userId, Guid id, string? title, bool? pinned, CancellationToken ct = default)
    {
        var conv = await _db.Conversations.FirstOrDefaultAsync(c => c.UserId == userId && c.Id == id, ct);
        if (conv is null) return false;
        if (title is not null) conv.Title = title;
        if (pinned is not null) conv.Pinned = pinned.Value;
        conv.Touch();
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var conv = await _db.Conversations.FirstOrDefaultAsync(c => c.UserId == userId && c.Id == id, ct);
        if (conv is null) return false;
        _db.Conversations.Remove(conv);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<Message> AppendMessageAsync(Guid conversationId, MessageRole role, string content, string? model, CancellationToken ct = default)
    {
        var conv = await _db.Conversations.FindAsync(new object?[] { conversationId }, ct);
        if (conv is null) throw new InvalidOperationException("Conversation not found");

        var msg = new Message
        {
            ConversationId = conversationId,
            Role = role,
            Content = content,
            Model = model
        };
        _db.Messages.Add(msg);

        if (role == MessageRole.User)
        {
            conv.Touch();
            if (conv.Title == "Nova conversa" && content.Length > 0)
            {
                conv.Title = content.Length <= 60 ? content : content[..60] + "...";
            }
        }

        await _db.SaveChangesAsync(ct);
        return msg;
    }

    public async Task FinalizeAssistantMessageAsync(Guid messageId, int? tokensIn, int? tokensOut, string? error, CancellationToken ct = default)
    {
        var msg = await _db.Messages.FindAsync(new object?[] { messageId }, ct);
        if (msg is null) return;
        msg.TokensIn = tokensIn;
        msg.TokensOut = tokensOut;
        msg.Error = error;
        await _db.SaveChangesAsync(ct);
    }
}
