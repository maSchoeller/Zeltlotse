using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Core.Persistenz.Migrationen;

/// <summary>
/// Der Betreiber gehört zu keiner Organisation — nach der ursprünglichen
/// Richtlinie durfte er deshalb auch keine Einladung schreiben, obwohl genau
/// das seine Aufgabe ist (er nimmt Träger auf und setzt deren Leitung ein).
///
/// Statt diesen Widerspruch mit einer Umgehung zu lösen, bekommt allein die
/// Einladungstabelle ein zweites, benanntes Recht. Freizeiten und Zuordnungen
/// bleiben ihm auch auf Datenbankebene verschlossen.
/// </summary>
public partial class BetreiberDarfEinladen : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP POLICY IF EXISTS einladung_mandant ON einladung;

            CREATE POLICY einladung_mandant ON einladung
                USING ("TenantId" = ANY (zeltlotse_mandanten())
                    OR coalesce(current_setting('app.betreiber', true), '') = '1')
                WITH CHECK ("TenantId" = ANY (zeltlotse_mandanten())
                    OR coalesce(current_setting('app.betreiber', true), '') = '1');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP POLICY IF EXISTS einladung_mandant ON einladung;

            CREATE POLICY einladung_mandant ON einladung
                USING ("TenantId" = ANY (zeltlotse_mandanten()))
                WITH CHECK ("TenantId" = ANY (zeltlotse_mandanten()));
            """);
    }
}
