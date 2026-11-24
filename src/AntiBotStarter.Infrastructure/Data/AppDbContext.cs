using AntiBotStarter.Domain.Email;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AntiBotStarter.Infrastructure.Data;

/// <summary>
/// Identity tables + Data Protection key ring (shared by every instance) + email outbox.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<IdentityUser>(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<EmailMessage> EmailMessages => Set<EmailMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<EmailMessage>(e =>
        {
            e.ToTable("EmailMessages");
            e.HasKey(m => m.Id);
            e.Property(m => m.Kind).HasMaxLength(32).IsRequired();
            e.Property(m => m.RecipientHash).HasMaxLength(64).IsRequired();
            e.Property(m => m.Subject).HasMaxLength(200).IsRequired();
            e.Property(m => m.LastError).HasMaxLength(500);
            e.Property(m => m.ConcurrencyStamp).IsConcurrencyToken();
            e.HasIndex(m => new { m.RecipientHash, m.CreatedUtc });
            e.HasIndex(m => new { m.Status, m.SendAfterUtc });
            e.HasIndex(m => m.CreatedUtc);
        });
    }
}
