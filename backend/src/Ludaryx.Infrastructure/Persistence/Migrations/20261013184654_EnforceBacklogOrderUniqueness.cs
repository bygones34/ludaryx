using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ludaryx.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceBacklogOrderUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF describes the backing unique index; PostgreSQL needs a deferrable
            // constraint so swaps can be checked at the end of the reorder transaction.
            migrationBuilder.Sql("""
                ALTER TABLE "UserGames" ADD CONSTRAINT "UQ_UserGames_UserId_BacklogOrder"
                UNIQUE ("UserId", "BacklogOrder") DEFERRABLE INITIALLY IMMEDIATE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The constraint owns its index, so drop the constraint rather than the index.
            migrationBuilder.Sql("ALTER TABLE \"UserGames\" DROP CONSTRAINT \"UQ_UserGames_UserId_BacklogOrder\";");
        }
    }
}
