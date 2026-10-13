using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Organisationen.Migrationen
{
    /// <inheritdoc />
    internal partial class OrgDokumente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dokument",
                schema: "organisationen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Titel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Dateiart = table.Column<short>(type: "smallint", nullable: false),
                    Groesse = table.Column<long>(type: "bigint", nullable: false),
                    HochgeladenAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dokument", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_dokument_OrganisationId",
                schema: "organisationen",
                table: "dokument",
                column: "OrganisationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dokument",
                schema: "organisationen");
        }
    }
}
