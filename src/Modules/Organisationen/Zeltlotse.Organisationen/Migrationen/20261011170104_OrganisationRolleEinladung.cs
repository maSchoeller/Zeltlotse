using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Organisationen.Migrationen
{
    /// <inheritdoc />
    internal partial class OrganisationRolleEinladung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "org_einladung",
                schema: "organisationen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Mail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Rolle = table.Column<short>(type: "smallint", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    AngelegtAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GueltigBis = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EingeladenVon = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_org_einladung", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "org_rolle",
                schema: "organisationen",
                columns: table => new
                {
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rolle = table.Column<short>(type: "smallint", nullable: false),
                    VergebenAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_org_rolle", x => new { x.OrganisationId, x.PersonId, x.Rolle });
                });

            migrationBuilder.CreateTable(
                name: "organisation",
                schema: "organisationen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AngelegtAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EingerichtetAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organisation", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_org_einladung_OrganisationId_Mail",
                schema: "organisationen",
                table: "org_einladung",
                columns: new[] { "OrganisationId", "Mail" });

            migrationBuilder.CreateIndex(
                name: "IX_org_einladung_TokenHash",
                schema: "organisationen",
                table: "org_einladung",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_org_rolle_PersonId",
                schema: "organisationen",
                table: "org_rolle",
                column: "PersonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "org_einladung",
                schema: "organisationen");

            migrationBuilder.DropTable(
                name: "org_rolle",
                schema: "organisationen");

            migrationBuilder.DropTable(
                name: "organisation",
                schema: "organisationen");
        }
    }
}
