using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Zeltlotse.Kanaele.Migrationen
{
    /// <inheritdoc />
    internal partial class Notizen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notiz",
                schema: "kanaele",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Laufnummer = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Art = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AngelegtAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ErledigtAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notiz", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notiz_FreizeitId_Art",
                schema: "kanaele",
                table: "notiz",
                columns: new[] { "FreizeitId", "Art" });

            migrationBuilder.CreateIndex(
                name: "IX_notiz_OrganisationId",
                schema: "kanaele",
                table: "notiz",
                column: "OrganisationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notiz",
                schema: "kanaele");
        }
    }
}
