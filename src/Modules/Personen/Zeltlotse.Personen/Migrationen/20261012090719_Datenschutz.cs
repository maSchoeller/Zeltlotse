using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Personen.Migrationen
{
    /// <inheritdoc />
    internal partial class Datenschutz : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Folgenummer",
                schema: "personen",
                table: "person",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "einsicht_protokoll",
                schema: "personen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BetroffenePersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    LesendePersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: true),
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: true),
                    Zweck = table.Column<short>(type: "smallint", nullable: false),
                    Am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Tag = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_einsicht_protokoll", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_einsicht_protokoll_BetroffenePersonId",
                schema: "personen",
                table: "einsicht_protokoll",
                column: "BetroffenePersonId");

            migrationBuilder.CreateIndex(
                name: "IX_einsicht_protokoll_je_tag",
                schema: "personen",
                table: "einsicht_protokoll",
                columns: new[] { "LesendePersonId", "BetroffenePersonId", "Tag", "Zweck" },
                unique: true,
                filter: "\"LesendePersonId\" <> '00000000-0000-7000-8000-0000000de1e7'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "einsicht_protokoll",
                schema: "personen");

            migrationBuilder.DropColumn(
                name: "Folgenummer",
                schema: "personen",
                table: "person");
        }
    }
}
