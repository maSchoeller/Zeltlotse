using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Kanaele.Migrationen
{
    /// <inheritdoc />
    internal partial class GastPush : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gast_push_abo",
                schema: "kanaele",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: false),
                    GeraeteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Stempel = table.Column<Guid>(type: "uuid", nullable: false),
                    Endpunkt = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    P256dh = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Auth = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AngelegtAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gast_push_abo", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_gast_push_abo_FreizeitId_Endpunkt",
                schema: "kanaele",
                table: "gast_push_abo",
                columns: new[] { "FreizeitId", "Endpunkt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gast_push_abo_OrganisationId",
                schema: "kanaele",
                table: "gast_push_abo",
                column: "OrganisationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gast_push_abo",
                schema: "kanaele");
        }
    }
}
