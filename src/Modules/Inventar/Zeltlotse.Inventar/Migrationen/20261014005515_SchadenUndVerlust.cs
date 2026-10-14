using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Inventar.Migrationen
{
    /// <inheritdoc />
    internal partial class SchadenUndVerlust : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "schaden_verlust",
                schema: "inventar",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Art = table.Column<short>(type: "smallint", nullable: false),
                    Bezeichnung = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ErfasstAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VorfallEintragId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schaden_verlust", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_schaden_verlust_FreizeitId",
                schema: "inventar",
                table: "schaden_verlust",
                column: "FreizeitId");

            migrationBuilder.CreateIndex(
                name: "IX_schaden_verlust_OrganisationId",
                schema: "inventar",
                table: "schaden_verlust",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_schaden_verlust_VorfallEintragId",
                schema: "inventar",
                table: "schaden_verlust",
                column: "VorfallEintragId",
                unique: true,
                filter: "\"VorfallEintragId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "schaden_verlust",
                schema: "inventar");
        }
    }
}
