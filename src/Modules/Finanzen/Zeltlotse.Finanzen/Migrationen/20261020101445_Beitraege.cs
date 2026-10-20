using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Finanzen.Migrationen
{
    /// <inheritdoc />
    internal partial class Beitraege : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "beitrag_vermerk",
                schema: "finanzen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    AbgehaktVon = table.Column<Guid>(type: "uuid", nullable: false),
                    AbgehaktAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_beitrag_vermerk", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_beitrag_vermerk_AbgehaktVon",
                schema: "finanzen",
                table: "beitrag_vermerk",
                column: "AbgehaktVon");

            migrationBuilder.CreateIndex(
                name: "IX_beitrag_vermerk_FreizeitId_PersonId",
                schema: "finanzen",
                table: "beitrag_vermerk",
                columns: new[] { "FreizeitId", "PersonId" },
                unique: true,
                filter: "\"PersonId\" <> '00000000-0000-7000-8000-0000000de1e7'");

            migrationBuilder.CreateIndex(
                name: "IX_beitrag_vermerk_OrganisationId",
                schema: "finanzen",
                table: "beitrag_vermerk",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_beitrag_vermerk_PersonId",
                schema: "finanzen",
                table: "beitrag_vermerk",
                column: "PersonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "beitrag_vermerk",
                schema: "finanzen");
        }
    }
}
