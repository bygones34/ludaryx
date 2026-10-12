using Ludaryx.Domain.Games;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ludaryx.Infrastructure.Persistence.Configurations;

public sealed class GamePlatformConfiguration : IEntityTypeConfiguration<GamePlatform>
{
    public void Configure(EntityTypeBuilder<GamePlatform> builder)
    {
        builder.ToTable("GamePlatforms");
        builder.HasKey(link => new { link.GameId, link.PlatformId });
        builder.HasOne(link => link.Game).WithMany(game => game.Platforms)
            .HasForeignKey(link => link.GameId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(link => link.Platform).WithMany()
            .HasForeignKey(link => link.PlatformId).OnDelete(DeleteBehavior.Restrict);
    }
}
