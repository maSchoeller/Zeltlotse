using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Personen.Migrationen
{
    /// <inheritdoc />
    internal partial class ZugriffOhnePerson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_einsicht_protokoll_je_tag",
                schema: "personen",
                table: "einsicht_protokoll");

            migrationBuilder.AlterColumn<Guid>(
                name: "LesendePersonId",
                schema: "personen",
                table: "einsicht_protokoll",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateIndex(
                name: "IX_einsicht_protokoll_je_tag",
                schema: "personen",
                table: "einsicht_protokoll",
                columns: new[] { "LesendePersonId", "BetroffenePersonId", "Tag", "Zweck" },
                unique: true,
                filter: "\"LesendePersonId\" IS NULL OR \"LesendePersonId\" <> '00000000-0000-7000-8000-0000000de1e7'")
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_einsicht_protokoll_je_tag",
                schema: "personen",
                table: "einsicht_protokoll");

            migrationBuilder.AlterColumn<Guid>(
                name: "LesendePersonId",
                schema: "personen",
                table: "einsicht_protokoll",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_einsicht_protokoll_je_tag",
                schema: "personen",
                table: "einsicht_protokoll",
                columns: new[] { "LesendePersonId", "BetroffenePersonId", "Tag", "Zweck" },
                unique: true,
                filter: "\"LesendePersonId\" <> '00000000-0000-7000-8000-0000000de1e7'");
        }
    }
}
