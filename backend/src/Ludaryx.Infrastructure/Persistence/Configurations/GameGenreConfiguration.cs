using Ludaryx.Domain.Games;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ludaryx.Infrastructure.Persistence.Configurations;

public sealed class GameGenreConfiguration : IEntityTypeConfiguration<GameGenre>
{
    public void Configure(EntityTypeBuilder<GameGenre> builder)
    {
        builder.ToTable("GameGenres");
        builder.HasKey(link => new { link.GameId, link.GenreId });
        builder.HasOne(link => link.Game).WithMany(game => game.Genres)
            .HasForeignKey(link => link.GameId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(link => link.Genre).WithMany()
            .HasForeignKey(link => link.GenreId).OnDelete(DeleteBehavior.Restrict);
    }
}
