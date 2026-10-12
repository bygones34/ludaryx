using Ludaryx.Domain.Games;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ludaryx.Infrastructure.Persistence.Configurations;

public sealed class PlatformConfiguration : IEntityTypeConfiguration<Platform>
{
    public void Configure(EntityTypeBuilder<Platform> builder)
    {
        builder.ToTable("Platforms", table => table.HasCheckConstraint("CK_Platforms_IgdbId", "\"IgdbId\" > 0"));
        builder.HasKey(platform => platform.Id);
        builder.Property(platform => platform.IgdbId).IsRequired();
        builder.HasIndex(platform => platform.IgdbId).IsUnique();
        builder.Property(platform => platform.Name).IsRequired();
    }
}
