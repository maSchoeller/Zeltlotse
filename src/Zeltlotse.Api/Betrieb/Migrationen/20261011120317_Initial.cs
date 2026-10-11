using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Api.Betrieb.Migrationen
{
    /// <inheritdoc />
    internal partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "betrieb");

            migrationBuilder.CreateTable(
                name: "sicherheitsprotokoll",
                schema: "betrieb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Zeitpunkt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Art = table.Column<short>(type: "smallint", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: true),
                    BezugId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sicherheitsprotokoll", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sicherheitsprotokoll_Zeitpunkt",
                schema: "betrieb",
                table: "sicherheitsprotokoll",
                column: "Zeitpunkt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sicherheitsprotokoll",
                schema: "betrieb");
        }
    }
}
