using Ludaryx.Domain.Games;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ludaryx.Infrastructure.Persistence.Configurations;

public sealed class GameConfiguration : IEntityTypeConfiguration<Game>
{
    public void Configure(EntityTypeBuilder<Game> builder)
    {
        builder.ToTable("Games", table => table.HasCheckConstraint("CK_Games_IgdbId", "\"IgdbId\" > 0"));
        builder.HasKey(game => game.Id);
        builder.Property(game => game.IgdbId).IsRequired();
        builder.HasIndex(game => game.IgdbId).IsUnique();
        builder.Property(game => game.Name).IsRequired();
        builder.Property(game => game.Slug).IsRequired();
        builder.Property(game => game.ReleaseDate).HasColumnType("date");
    }
}
