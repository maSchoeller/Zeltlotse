using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Freizeiten.Migrationen
{
    /// <inheritdoc />
    internal partial class TeamNotizen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "team_notiz",
                schema: "freizeiten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnmeldungId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    VerfasstVon = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<byte[]>(type: "bytea", nullable: false),
                    AngelegtAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_team_notiz", x => x.Id);
                    table.ForeignKey(
                        name: "FK_team_notiz_anmeldung_AnmeldungId",
                        column: x => x.AnmeldungId,
                        principalSchema: "freizeiten",
                        principalTable: "anmeldung",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_team_notiz_AnmeldungId",
                schema: "freizeiten",
                table: "team_notiz",
                column: "AnmeldungId");

            migrationBuilder.CreateIndex(
                name: "IX_team_notiz_FreizeitId_AnmeldungId",
                schema: "freizeiten",
                table: "team_notiz",
                columns: new[] { "FreizeitId", "AnmeldungId" });

            migrationBuilder.CreateIndex(
                name: "IX_team_notiz_OrganisationId",
                schema: "freizeiten",
                table: "team_notiz",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_team_notiz_PersonId",
                schema: "freizeiten",
                table: "team_notiz",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_team_notiz_VerfasstVon",
                schema: "freizeiten",
                table: "team_notiz",
                column: "VerfasstVon");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "team_notiz",
                schema: "freizeiten");
        }
    }
}
