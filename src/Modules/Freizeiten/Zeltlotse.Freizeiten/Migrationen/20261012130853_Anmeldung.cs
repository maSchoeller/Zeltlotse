using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Freizeiten.Migrationen
{
    /// <inheritdoc />
    internal partial class Anmeldung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Teilnahmebedingungen",
                schema: "freizeiten",
                table: "freizeit",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "anmeldung",
                schema: "freizeiten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Art = table.Column<short>(type: "smallint", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    EingegangenAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TeilnahmebedingungenBestaetigtAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Folgenummer = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_anmeldung", x => x.Id);
                    table.ForeignKey(
                        name: "FK_anmeldung_freizeit_FreizeitId",
                        column: x => x.FreizeitId,
                        principalSchema: "freizeiten",
                        principalTable: "freizeit",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_anmeldung_je_person",
                schema: "freizeiten",
                table: "anmeldung",
                columns: new[] { "FreizeitId", "PersonId" },
                unique: true,
                filter: "\"PersonId\" <> '00000000-0000-7000-8000-0000000de1e7'");

            migrationBuilder.CreateIndex(
                name: "IX_anmeldung_OrganisationId",
                schema: "freizeiten",
                table: "anmeldung",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_anmeldung_PersonId",
                schema: "freizeiten",
                table: "anmeldung",
                column: "PersonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "anmeldung",
                schema: "freizeiten");

            migrationBuilder.DropColumn(
                name: "Teilnahmebedingungen",
                schema: "freizeiten",
                table: "freizeit");
        }
    }
}
