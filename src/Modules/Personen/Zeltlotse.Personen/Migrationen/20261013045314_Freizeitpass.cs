using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Personen.Migrationen
{
    /// <inheritdoc />
    internal partial class Freizeitpass : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "freizeitpass_antwort",
                schema: "personen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kennung = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Wert = table.Column<byte[]>(type: "bytea", nullable: true),
                    Sonstiges = table.Column<byte[]>(type: "bytea", nullable: true),
                    BestaetigtAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_freizeitpass_antwort", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_freizeitpass_antwort_PersonId_Kennung",
                schema: "personen",
                table: "freizeitpass_antwort",
                columns: new[] { "PersonId", "Kennung" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "freizeitpass_antwort",
                schema: "personen");
        }
    }
}
