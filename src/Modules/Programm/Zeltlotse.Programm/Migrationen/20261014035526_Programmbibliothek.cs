using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Programm.Migrationen
{
    /// <inheritdoc />
    internal partial class Programmbibliothek : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BibliotheksEintragId",
                schema: "programm",
                table: "programmpunkt",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "bibliotheks_eintrag",
                schema: "programm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Titel = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    AbgelegtAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Aufgaben = table.Column<string>(type: "jsonb", nullable: true),
                    Material = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bibliotheks_eintrag", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_programmpunkt_BibliotheksEintragId",
                schema: "programm",
                table: "programmpunkt",
                column: "BibliotheksEintragId");

            migrationBuilder.CreateIndex(
                name: "IX_bibliotheks_eintrag_OrganisationId",
                schema: "programm",
                table: "bibliotheks_eintrag",
                column: "OrganisationId");

            migrationBuilder.AddForeignKey(
                name: "FK_programmpunkt_bibliotheks_eintrag_BibliotheksEintragId",
                schema: "programm",
                table: "programmpunkt",
                column: "BibliotheksEintragId",
                principalSchema: "programm",
                principalTable: "bibliotheks_eintrag",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_programmpunkt_bibliotheks_eintrag_BibliotheksEintragId",
                schema: "programm",
                table: "programmpunkt");

            migrationBuilder.DropTable(
                name: "bibliotheks_eintrag",
                schema: "programm");

            migrationBuilder.DropIndex(
                name: "IX_programmpunkt_BibliotheksEintragId",
                schema: "programm",
                table: "programmpunkt");

            migrationBuilder.DropColumn(
                name: "BibliotheksEintragId",
                schema: "programm",
                table: "programmpunkt");
        }
    }
}
