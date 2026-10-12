using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Freizeiten.Migrationen
{
    /// <inheritdoc />
    internal partial class AnmeldungEntscheidung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KontaktMail",
                schema: "freizeiten",
                table: "freizeit",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KontaktTelefon",
                schema: "freizeiten",
                table: "freizeit",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EntschiedenAm",
                schema: "freizeiten",
                table: "anmeldung",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KontaktMail",
                schema: "freizeiten",
                table: "freizeit");

            migrationBuilder.DropColumn(
                name: "KontaktTelefon",
                schema: "freizeiten",
                table: "freizeit");

            migrationBuilder.DropColumn(
                name: "EntschiedenAm",
                schema: "freizeiten",
                table: "anmeldung");
        }
    }
}
