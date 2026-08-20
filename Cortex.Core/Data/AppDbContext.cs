using Cortex.Core.Objects;
using Microsoft.EntityFrameworkCore;

namespace Cortex.Core.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ProviderKey> ProviderKeys => Set<ProviderKey>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Memory> Memories => Set<Memory>();
    public DbSet<Project> Projects => Set<Project>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasColumnType("citext").IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Provider).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(x => x.ProviderUid).HasMaxLength(128).IsRequired();
            e.HasIndex(x => new { x.Provider, x.ProviderUid }).IsUnique();
            e.Property(x => x.CreatedAt).HasColumnType("timestamptz");
        });

        b.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_tokens");
            e.HasKey(x => x.Id);
            e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.ExpiresAt).HasColumnType("timestamptz");
            e.Property(x => x.RevokedAt).HasColumnType("timestamptz");
            e.Property(x => x.CreatedAt).HasColumnType("timestamptz");
            e.HasOne(x => x.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ProviderKey>(e =>
        {
            e.ToTable("provider_keys");
            e.HasKey(x => x.Id);
            e.Property(x => x.Provider).HasMaxLength(32).IsRequired();
            e.HasIndex(x => new { x.UserId, x.Provider }).IsUnique();
            e.Property(x => x.CreatedAt).HasColumnType("timestamptz");
            e.Property(x => x.UpdatedAt).HasColumnType("timestamptz");
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Conversation>(e =>
        {
            e.ToTable("conversations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Provider).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(x => x.Model).HasMaxLength(128).IsRequired();
            e.Property(x => x.FallbackProvider).HasMaxLength(32);
            e.Property(x => x.FallbackModel).HasMaxLength(128);
            e.Property(x => x.CreatedAt).HasColumnType("timestamptz");
            e.Property(x => x.UpdatedAt).HasColumnType("timestamptz");
            e.HasOne(x => x.User)
                .WithMany(u => u.Conversations)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            // Filing into a project/folder; deleting the project unfiles (SetNull), never deletes chats.
            e.HasOne(x => x.Project)
                .WithMany()
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.UserId, x.UpdatedAt });
            e.HasIndex(x => x.ProjectId);
        });

        b.Entity<Message>(e =>
        {
            e.ToTable("messages");
            e.HasKey(x => x.Id);
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(16).IsRequired();
            e.Property(x => x.Content).IsRequired();
            e.Property(x => x.Model).HasMaxLength(128);
            e.Property(x => x.Cost).HasPrecision(18, 6);
            e.Property(x => x.CreatedAt).HasColumnType("timestamptz");
            e.HasOne(x => x.Conversation)
                .WithMany(c => c.Messages)
                .HasForeignKey(x => x.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ConversationId, x.CreatedAt });
        });

        b.Entity<Memory>(e =>
        {
            e.ToTable("memories");
            e.HasKey(x => x.Id);
            e.Property(x => x.Scope).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(16).IsRequired();
            e.Property(x => x.Content).IsRequired();
            e.Property(x => x.CreatedAt).HasColumnType("timestamptz");
            e.Property(x => x.UpdatedAt).HasColumnType("timestamptz");
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Conversation)
                .WithMany()
                .HasForeignKey(x => x.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.UserId, x.Scope });
            e.HasIndex(x => new { x.UserId, x.ConversationId });
        });

        b.Entity<Project>(e =>
        {
            e.ToTable("projects");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.CreatedAt).HasColumnType("timestamptz");
            e.Property(x => x.UpdatedAt).HasColumnType("timestamptz");
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            // Folders cascade with their project (2 levels; no cycles possible).
            e.HasOne(x => x.Parent)
                .WithMany()
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.ParentId);
        });
    }
}
