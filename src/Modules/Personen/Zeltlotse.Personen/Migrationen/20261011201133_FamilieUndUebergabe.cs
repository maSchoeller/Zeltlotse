using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Personen.Migrationen
{
    /// <inheritdoc />
    internal partial class FamilieUndUebergabe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FamilieId",
                schema: "personen",
                table: "person",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "Geburtsdatum",
                schema: "personen",
                table: "person",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "familie",
                schema: "personen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AngelegtAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_familie", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "familien_einladung",
                schema: "personen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilieId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_familien_einladung", x => x.Id);
                    table.ForeignKey(
                        name: "FK_familien_einladung_familie_FamilieId",
                        column: x => x.FamilieId,
                        principalSchema: "personen",
                        principalTable: "familie",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "familien_elternteil",
                schema: "personen",
                columns: table => new
                {
                    FamilieId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_familien_elternteil", x => new { x.FamilieId, x.PersonId });
                    table.ForeignKey(
                        name: "FK_familien_elternteil_familie_FamilieId",
                        column: x => x.FamilieId,
                        principalSchema: "personen",
                        principalTable: "familie",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "profil_uebergabe",
                schema: "personen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilieId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Mail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    TokenHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    AngelegtAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GueltigBis = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EingeladenVon = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    Folgenummer = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_profil_uebergabe", x => x.Id);
                    table.ForeignKey(
                        name: "FK_profil_uebergabe_familie_FamilieId",
                        column: x => x.FamilieId,
                        principalSchema: "personen",
                        principalTable: "familie",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_person_FamilieId",
                schema: "personen",
                table: "person",
                column: "FamilieId");

            migrationBuilder.CreateIndex(
                name: "IX_familien_einladung_FamilieId",
                schema: "personen",
                table: "familien_einladung",
                column: "FamilieId");

            migrationBuilder.CreateIndex(
                name: "IX_familien_einladung_TokenHash",
                schema: "personen",
                table: "familien_einladung",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_familien_elternteil_PersonId",
                schema: "personen",
                table: "familien_elternteil",
                column: "PersonId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_profil_uebergabe_FamilieId",
                schema: "personen",
                table: "profil_uebergabe",
                column: "FamilieId");

            migrationBuilder.CreateIndex(
                name: "IX_profil_uebergabe_PersonId",
                schema: "personen",
                table: "profil_uebergabe",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_profil_uebergabe_TokenHash",
                schema: "personen",
                table: "profil_uebergabe",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "familien_einladung",
                schema: "personen");

            migrationBuilder.DropTable(
                name: "familien_elternteil",
                schema: "personen");

            migrationBuilder.DropTable(
                name: "profil_uebergabe",
                schema: "personen");

            migrationBuilder.DropTable(
                name: "familie",
                schema: "personen");

            migrationBuilder.DropIndex(
                name: "IX_person_FamilieId",
                schema: "personen",
                table: "person");

            migrationBuilder.DropColumn(
                name: "FamilieId",
                schema: "personen",
                table: "person");

            migrationBuilder.DropColumn(
                name: "Geburtsdatum",
                schema: "personen",
                table: "person");
        }
    }
}
