using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Zeltlotse.Kanaele.Migrationen
{
    /// <inheritdoc />
    internal partial class Kanaele : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "nachricht",
                schema: "kanaele",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Laufnummer = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kanal = table.Column<int>(type: "integer", nullable: false),
                    AutorPersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    GesendetAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Folgenummer = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_nachricht", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "push_abo",
                schema: "kanaele",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Endpunkt = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    P256dh = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Auth = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AngelegtAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_push_abo", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_nachricht_AutorPersonId",
                schema: "kanaele",
                table: "nachricht",
                column: "AutorPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_nachricht_FreizeitId_Kanal_GesendetAm",
                schema: "kanaele",
                table: "nachricht",
                columns: new[] { "FreizeitId", "Kanal", "GesendetAm" });

            migrationBuilder.CreateIndex(
                name: "IX_nachricht_OrganisationId",
                schema: "kanaele",
                table: "nachricht",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_push_abo_FreizeitId_Endpunkt",
                schema: "kanaele",
                table: "push_abo",
                columns: new[] { "FreizeitId", "Endpunkt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_push_abo_OrganisationId",
                schema: "kanaele",
                table: "push_abo",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_push_abo_PersonId",
                schema: "kanaele",
                table: "push_abo",
                column: "PersonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "nachricht",
                schema: "kanaele");

            migrationBuilder.DropTable(
                name: "push_abo",
                schema: "kanaele");
        }
    }
}
