using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Freizeiten.Migrationen
{
    /// <inheritdoc />
    internal partial class Dokumente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dokument",
                schema: "freizeiten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrgDokumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Titel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Sichtbarkeit = table.Column<short>(type: "smallint", nullable: false),
                    Dateiart = table.Column<short>(type: "smallint", nullable: true),
                    Groesse = table.Column<long>(type: "bigint", nullable: true),
                    HochgeladenAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dokument", x => x.Id);
                    table.ForeignKey(
                        name: "FK_dokument_freizeit_FreizeitId",
                        column: x => x.FreizeitId,
                        principalSchema: "freizeiten",
                        principalTable: "freizeit",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_dokument_einbindung",
                schema: "freizeiten",
                table: "dokument",
                columns: new[] { "FreizeitId", "OrgDokumentId" },
                unique: true,
                filter: "\"OrgDokumentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_dokument_FreizeitId",
                schema: "freizeiten",
                table: "dokument",
                column: "FreizeitId");

            migrationBuilder.CreateIndex(
                name: "IX_dokument_OrganisationId",
                schema: "freizeiten",
                table: "dokument",
                column: "OrganisationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dokument",
                schema: "freizeiten");
        }
    }
}
