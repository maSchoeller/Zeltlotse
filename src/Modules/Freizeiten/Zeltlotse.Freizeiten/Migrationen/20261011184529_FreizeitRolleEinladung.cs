using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Freizeiten.Migrationen
{
    /// <inheritdoc />
    internal partial class FreizeitRolleEinladung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "freizeit",
                schema: "freizeiten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ErsterTag = table.Column<DateOnly>(type: "date", nullable: false),
                    LetzterTag = table.Column<DateOnly>(type: "date", nullable: false),
                    FreigabeIntervallTage = table.Column<int>(type: "integer", nullable: false),
                    AngelegtAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AngelegtVon = table.Column<Guid>(type: "uuid", nullable: false),
                    AbgeschlossenAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Folgenummer = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_freizeit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "leitungs_einladung",
                schema: "freizeiten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Mail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    TokenHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    AngelegtAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GueltigBis = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EingeladenVon = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leitungs_einladung", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "freizeit_rolle",
                schema: "freizeiten",
                columns: table => new
                {
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rolle = table.Column<short>(type: "smallint", nullable: false),
                    VergebenAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_freizeit_rolle", x => new { x.FreizeitId, x.PersonId });
                    table.ForeignKey(
                        name: "FK_freizeit_rolle_freizeit_FreizeitId",
                        column: x => x.FreizeitId,
                        principalSchema: "freizeiten",
                        principalTable: "freizeit",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_freizeit_OrganisationId",
                schema: "freizeiten",
                table: "freizeit",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_freizeit_rolle_PersonId",
                schema: "freizeiten",
                table: "freizeit_rolle",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_leitungs_einladung_FreizeitId_Mail",
                schema: "freizeiten",
                table: "leitungs_einladung",
                columns: new[] { "FreizeitId", "Mail" });

            migrationBuilder.CreateIndex(
                name: "IX_leitungs_einladung_TokenHash",
                schema: "freizeiten",
                table: "leitungs_einladung",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "freizeit_rolle",
                schema: "freizeiten");

            migrationBuilder.DropTable(
                name: "leitungs_einladung",
                schema: "freizeiten");

            migrationBuilder.DropTable(
                name: "freizeit",
                schema: "freizeiten");
        }
    }
}
