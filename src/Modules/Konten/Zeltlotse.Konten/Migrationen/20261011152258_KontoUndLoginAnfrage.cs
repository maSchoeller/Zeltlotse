using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Konten.Migrationen
{
    /// <inheritdoc />
    internal partial class KontoUndLoginAnfrage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "konto",
                schema: "konten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Mail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    PasswortHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SitzungsStempel = table.Column<Guid>(type: "uuid", nullable: false),
                    AngelegtAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_konto", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "login_anfrage",
                schema: "konten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Mail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    TokenHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    CodeHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    AngelegtAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GueltigBis = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Fehlversuche = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_login_anfrage", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_konto_Mail",
                schema: "konten",
                table: "konto",
                column: "Mail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_konto_PersonId",
                schema: "konten",
                table: "konto",
                column: "PersonId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_login_anfrage_Mail",
                schema: "konten",
                table: "login_anfrage",
                column: "Mail");

            migrationBuilder.CreateIndex(
                name: "IX_login_anfrage_TokenHash",
                schema: "konten",
                table: "login_anfrage",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "konto",
                schema: "konten");

            migrationBuilder.DropTable(
                name: "login_anfrage",
                schema: "konten");
        }
    }
}
