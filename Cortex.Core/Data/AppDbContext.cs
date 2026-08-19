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
            e.Property(x => x.CreatedAt).HasColumnType("timestamptz");
            e.Property(x => x.UpdatedAt).HasColumnType("timestamptz");
            e.HasOne(x => x.User)
                .WithMany(u => u.Conversations)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.UserId, x.UpdatedAt });
        });

        b.Entity<Message>(e =>
        {
            e.ToTable("messages");
            e.HasKey(x => x.Id);
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(16).IsRequired();
            e.Property(x => x.Content).IsRequired();
            e.Property(x => x.Model).HasMaxLength(128);
            e.Property(x => x.CreatedAt).HasColumnType("timestamptz");
            e.HasOne(x => x.Conversation)
                .WithMany(c => c.Messages)
                .HasForeignKey(x => x.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ConversationId, x.CreatedAt });
        });
    }
}
