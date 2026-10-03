using Ludaryx.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Ludaryx.Infrastructure.Persistence;

public class LudaryxDbContext(DbContextOptions<LudaryxDbContext> options)
    : IdentityUserContext<ApplicationUser, Guid>(options)
{
    public DbSet<RefreshTokenRecord> RefreshTokens => Set<RefreshTokenRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(user =>
        {
            user.Property(u => u.UserName).IsRequired().HasMaxLength(30);
            user.Property(u => u.NormalizedUserName).IsRequired().HasMaxLength(30);
            user.Property(u => u.Email).IsRequired();
            user.Property(u => u.NormalizedEmail).IsRequired();
            user.Property(u => u.DisplayName).IsRequired();
            user.Property(u => u.CreatedAt).IsRequired();
            user.HasIndex(u => u.NormalizedEmail).IsUnique();
        });

        builder.Entity<RefreshTokenRecord>(token =>
        {
            token.HasKey(record => record.Id);
            token.Property(record => record.TokenHash).IsRequired().HasMaxLength(32);
            token.HasIndex(record => record.TokenHash).IsUnique();
            token.HasIndex(record => record.FamilyId);
            token.HasIndex(record => record.UserId);
            token.HasOne(record => record.User)
                .WithMany()
                .HasForeignKey(record => record.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
