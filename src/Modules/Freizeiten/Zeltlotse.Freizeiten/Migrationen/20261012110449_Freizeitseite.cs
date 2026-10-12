using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Freizeiten.Migrationen
{
    /// <inheritdoc />
    internal partial class Freizeitseite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Beschreibung",
                schema: "freizeiten",
                table: "freizeit",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HoechstzahlTeilnehmende",
                schema: "freizeiten",
                table: "freizeit",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Ort",
                schema: "freizeiten",
                table: "freizeit",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "freizeiten",
                table: "freizeit",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "anmeldelink",
                schema: "freizeiten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Art = table.Column<short>(type: "smallint", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    TokenGeschuetzt = table.Column<byte[]>(type: "bytea", nullable: false),
                    ErzeugtAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_anmeldelink", x => x.Id);
                    table.ForeignKey(
                        name: "FK_anmeldelink_freizeit_FreizeitId",
                        column: x => x.FreizeitId,
                        principalSchema: "freizeiten",
                        principalTable: "freizeit",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_anmeldelink_FreizeitId_Art",
                schema: "freizeiten",
                table: "anmeldelink",
                columns: new[] { "FreizeitId", "Art" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_anmeldelink_TokenHash",
                schema: "freizeiten",
                table: "anmeldelink",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "anmeldelink",
                schema: "freizeiten");

            migrationBuilder.DropColumn(
                name: "Beschreibung",
                schema: "freizeiten",
                table: "freizeit");

            migrationBuilder.DropColumn(
                name: "HoechstzahlTeilnehmende",
                schema: "freizeiten",
                table: "freizeit");

            migrationBuilder.DropColumn(
                name: "Ort",
                schema: "freizeiten",
                table: "freizeit");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "freizeiten",
                table: "freizeit");
        }
    }
}
