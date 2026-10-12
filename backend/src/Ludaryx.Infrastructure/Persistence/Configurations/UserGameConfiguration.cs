using Ludaryx.Domain.Library;
using Ludaryx.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ludaryx.Infrastructure.Persistence.Configurations;

public sealed class UserGameConfiguration : IEntityTypeConfiguration<UserGame>
{
    public void Configure(EntityTypeBuilder<UserGame> builder)
    {
        builder.ToTable("UserGames", table =>
        {
            table.HasCheckConstraint("CK_UserGames_Status", "\"Status\" IN ('Backlog', 'Playing', 'Completed', 'OnHold', 'Dropped')");
            table.HasCheckConstraint("CK_UserGames_Priority", "\"Priority\" IS NULL OR \"Priority\" IN ('Low', 'Medium', 'High')");
            table.HasCheckConstraint("CK_UserGames_Rating", "\"Rating\" IS NULL OR \"Rating\" BETWEEN 1 AND 10");
            table.HasCheckConstraint("CK_UserGames_BacklogFields",
                "(\"Status\" = 'Backlog' AND \"BacklogOrder\" IS NOT NULL AND \"BacklogOrder\" > 0) OR (\"Status\" <> 'Backlog' AND \"BacklogOrder\" IS NULL AND \"Priority\" IS NULL)");
        });
        builder.HasKey(item => item.Id);
        builder.HasIndex(item => new { item.UserId, item.GameId }).IsUnique();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.Game).WithMany().HasForeignKey(item => item.GameId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(item => item.Status).HasConversion<string>().HasColumnType("text").IsRequired();
        builder.Property(item => item.Priority).HasConversion<string>().HasColumnType("text");
        builder.Property(item => item.PersonalNote).HasMaxLength(1000);
        builder.Property(item => item.AddedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(item => item.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(item => item.StartedAt).HasColumnType("timestamp with time zone");
        builder.Property(item => item.CompletedAt).HasColumnType("timestamp with time zone");
    }
}
