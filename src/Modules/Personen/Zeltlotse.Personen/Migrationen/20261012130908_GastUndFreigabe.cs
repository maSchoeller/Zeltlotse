using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Personen.Migrationen
{
    /// <inheritdoc />
    internal partial class GastUndFreigabe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "freigabe",
                schema: "personen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnmeldungId = table.Column<Guid>(type: "uuid", nullable: false),
                    Stufe = table.Column<short>(type: "smallint", nullable: false),
                    GueltigBis = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AngelegtAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_freigabe", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "gast_zugang",
                schema: "personen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElternPersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Mail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    TokenHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    AngelegtAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UebernommenAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Folgenummer = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gast_zugang", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_freigabe_AnmeldungId",
                schema: "personen",
                table: "freigabe",
                column: "AnmeldungId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_freigabe_OrganisationId_FreizeitId",
                schema: "personen",
                table: "freigabe",
                columns: new[] { "OrganisationId", "FreizeitId" });

            migrationBuilder.CreateIndex(
                name: "IX_freigabe_PersonId",
                schema: "personen",
                table: "freigabe",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_gast_zugang_ElternPersonId",
                schema: "personen",
                table: "gast_zugang",
                column: "ElternPersonId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gast_zugang_TokenHash",
                schema: "personen",
                table: "gast_zugang",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "freigabe",
                schema: "personen");

            migrationBuilder.DropTable(
                name: "gast_zugang",
                schema: "personen");
        }
    }
}
