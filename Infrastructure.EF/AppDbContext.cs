using Microsoft.EntityFrameworkCore;
using Model;

namespace Infrastructure.EF;

public sealed class AppDbContext : DbContext
{
    public DbSet<UserMailbox> UserMailboxes => Set<UserMailbox>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var e = modelBuilder.Entity<UserMailbox>();

        e.HasKey(x => x.MailboxAddress);
        
        e.Property(x => x.ExpiresAt)
            .HasColumnType("timestamptz");
        
        e.HasIndex(x => x.User)
            .HasFilter(@"""IsCurrent"" = TRUE")
            .IsUnique();
        
        e.HasIndex(x => x.ExpiresAt);
    }
}