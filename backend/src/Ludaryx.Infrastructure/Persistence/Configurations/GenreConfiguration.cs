using Ludaryx.Domain.Games;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ludaryx.Infrastructure.Persistence.Configurations;

public sealed class GenreConfiguration : IEntityTypeConfiguration<Genre>
{
    public void Configure(EntityTypeBuilder<Genre> builder)
    {
        builder.ToTable("Genres", table => table.HasCheckConstraint("CK_Genres_IgdbId", "\"IgdbId\" > 0"));
        builder.HasKey(genre => genre.Id);
        builder.Property(genre => genre.IgdbId).IsRequired();
        builder.HasIndex(genre => genre.IgdbId).IsUnique();
        builder.Property(genre => genre.Name).IsRequired();
    }
}
