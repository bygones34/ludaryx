using Ludaryx.Infrastructure.Identity;
using Ludaryx.Domain.Games;
using Ludaryx.Domain.Library;
using Ludaryx.Infrastructure.Persistence.Configurations;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Ludaryx.Infrastructure.Persistence;

public class LudaryxDbContext(DbContextOptions<LudaryxDbContext> options)
    : IdentityUserContext<ApplicationUser, Guid>(options)
{
    public DbSet<RefreshTokenRecord> RefreshTokens => Set<RefreshTokenRecord>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<Platform> Platforms => Set<Platform>();
    public DbSet<GameGenre> GameGenres => Set<GameGenre>();
    public DbSet<GamePlatform> GamePlatforms => Set<GamePlatform>();
    public DbSet<UserGame> UserGames => Set<UserGame>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfiguration(new GameConfiguration());
        builder.ApplyConfiguration(new GenreConfiguration());
        builder.ApplyConfiguration(new PlatformConfiguration());
        builder.ApplyConfiguration(new GameGenreConfiguration());
        builder.ApplyConfiguration(new GamePlatformConfiguration());
        builder.ApplyConfiguration(new UserGameConfiguration());

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
