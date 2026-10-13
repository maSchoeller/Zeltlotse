using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Personen.Migrationen
{
    /// <inheritdoc />
    internal partial class FreizeitpassErinnerung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "freizeitpass_erinnerung",
                schema: "personen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    ErinnertVon = table.Column<Guid>(type: "uuid", nullable: false),
                    ErinnertAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Folgenummer = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_freizeitpass_erinnerung", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_freizeitpass_erinnerung_FreizeitId_PersonId",
                schema: "personen",
                table: "freizeitpass_erinnerung",
                columns: new[] { "FreizeitId", "PersonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_freizeitpass_erinnerung_PersonId",
                schema: "personen",
                table: "freizeitpass_erinnerung",
                column: "PersonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "freizeitpass_erinnerung",
                schema: "personen");
        }
    }
}
