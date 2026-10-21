using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Freizeiten.Migrationen
{
    /// <inheritdoc />
    internal partial class QrZugang : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "qr_zugang",
                schema: "freizeiten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    TokenGeschuetzt = table.Column<byte[]>(type: "bytea", nullable: false),
                    Stempel = table.Column<Guid>(type: "uuid", nullable: false),
                    ErzeugtAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_qr_zugang", x => x.Id);
                    table.ForeignKey(
                        name: "FK_qr_zugang_freizeit_FreizeitId",
                        column: x => x.FreizeitId,
                        principalSchema: "freizeiten",
                        principalTable: "freizeit",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_qr_zugang_FreizeitId",
                schema: "freizeiten",
                table: "qr_zugang",
                column: "FreizeitId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_qr_zugang_OrganisationId",
                schema: "freizeiten",
                table: "qr_zugang",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_qr_zugang_TokenHash",
                schema: "freizeiten",
                table: "qr_zugang",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "qr_zugang",
                schema: "freizeiten");
        }
    }
}
