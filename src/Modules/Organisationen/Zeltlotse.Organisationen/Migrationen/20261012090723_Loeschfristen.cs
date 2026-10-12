using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Organisationen.Migrationen
{
    /// <inheritdoc />
    internal partial class Loeschfristen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "loeschfristen",
                schema: "organisationen",
                columns: table => new
                {
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreizeitDatenMonate = table.Column<int>(type: "integer", nullable: false),
                    NachweisBestaetigungMonate = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_loeschfristen", x => x.OrganisationId);
                });

            // NFR-5: Bestehende Organisationen bekommen die Vorgabewerte (12 Monate), wie neue beim Anlegen.
            migrationBuilder.Sql(
                "INSERT INTO organisationen.loeschfristen (\"OrganisationId\", \"FreizeitDatenMonate\", \"NachweisBestaetigungMonate\") " +
                "SELECT \"Id\", 12, 12 FROM organisationen.organisation ON CONFLICT DO NOTHING;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "loeschfristen",
                schema: "organisationen");
        }
    }
}
