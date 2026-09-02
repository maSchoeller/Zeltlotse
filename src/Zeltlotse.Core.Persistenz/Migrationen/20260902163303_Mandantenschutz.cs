using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Core.Persistenz.Migrationen;

/// <summary>
/// Das zweite Netz der Mandantentrennung: Row-Level-Security in Azure SQL.
///
/// Anders als PostgreSQL kennt SQL Server keinen automatischen Bypass für
/// privilegierte Verbindungen (weder Rolle noch Tabelleneigentümer). Die
/// Ausnahme für <see cref="Zeltlotse.Core.Persistenz.SystemDatenbank"/> ist
/// deshalb explizit über die Sitzungsvariable <c>system_bypass</c> abgebildet
/// — bei PostgreSQL ergab sie sich implizit aus der nicht umgestellten Rolle.
///
/// Die Einladungstabelle bekommt eine eigene Prädikatfunktion mit der
/// Betreiber-Ausnahme; Freizeiten und Zuordnungen bleiben ihm verschlossen.
/// </summary>
public partial class Mandantenschutz : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Liest die Organisationen des angemeldeten Nutzers aus der
        // Sitzungsvariablen. Leer oder nicht gesetzt bedeutet: keine Zeile.
        // TRY_CAST statt CAST, damit eine leere oder ungültige Zeichenkette
        // zu NULL (= kein Treffer) wird statt einen Fehler auszulösen.
        migrationBuilder.Sql("""
            CREATE FUNCTION dbo.fn_zeltlotse_mandanten(@TenantId uniqueidentifier)
            RETURNS TABLE
            WITH SCHEMABINDING
            AS RETURN
                SELECT 1 AS ergebnis
                WHERE CAST(SESSION_CONTEXT(N'system_bypass') AS int) = 1
                    OR EXISTS (
                        SELECT 1
                        FROM STRING_SPLIT(CAST(SESSION_CONTEXT(N'tenant_ids') AS nvarchar(max)), ',')
                        WHERE TRY_CAST(value AS uniqueidentifier) = @TenantId
                    );
            """);

        // Zusätzlich zur Mandantenprüfung: Der Betreiber gehört zu keiner
        // Organisation, muss aber Einladungen für jede schreiben können.
        // Nur diese Tabelle kennt das zweite Recht.
        migrationBuilder.Sql("""
            CREATE FUNCTION dbo.fn_zeltlotse_einladung_mandanten(@TenantId uniqueidentifier)
            RETURNS TABLE
            WITH SCHEMABINDING
            AS RETURN
                SELECT 1 AS ergebnis
                WHERE CAST(SESSION_CONTEXT(N'system_bypass') AS int) = 1
                    OR CAST(SESSION_CONTEXT(N'betreiber') AS nvarchar(max)) = '1'
                    OR EXISTS (
                        SELECT 1
                        FROM STRING_SPLIT(CAST(SESSION_CONTEXT(N'tenant_ids') AS nvarchar(max)), ',')
                        WHERE TRY_CAST(value AS uniqueidentifier) = @TenantId
                    );
            """);

        migrationBuilder.Sql("""
            CREATE SECURITY POLICY dbo.ZeltlotseMandantenschutz
                ADD FILTER PREDICATE dbo.fn_zeltlotse_mandanten(TenantId) ON dbo.freizeit,
                ADD BLOCK PREDICATE dbo.fn_zeltlotse_mandanten(TenantId) ON dbo.freizeit AFTER INSERT,
                ADD BLOCK PREDICATE dbo.fn_zeltlotse_mandanten(TenantId) ON dbo.freizeit AFTER UPDATE,
                ADD FILTER PREDICATE dbo.fn_zeltlotse_mandanten(TenantId) ON dbo.freizeit_zuordnung,
                ADD BLOCK PREDICATE dbo.fn_zeltlotse_mandanten(TenantId) ON dbo.freizeit_zuordnung AFTER INSERT,
                ADD BLOCK PREDICATE dbo.fn_zeltlotse_mandanten(TenantId) ON dbo.freizeit_zuordnung AFTER UPDATE,
                ADD FILTER PREDICATE dbo.fn_zeltlotse_einladung_mandanten(TenantId) ON dbo.einladung,
                ADD BLOCK PREDICATE dbo.fn_zeltlotse_einladung_mandanten(TenantId) ON dbo.einladung AFTER INSERT,
                ADD BLOCK PREDICATE dbo.fn_zeltlotse_einladung_mandanten(TenantId) ON dbo.einladung AFTER UPDATE
            WITH (STATE = ON);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP SECURITY POLICY IF EXISTS dbo.ZeltlotseMandantenschutz;");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS dbo.fn_zeltlotse_einladung_mandanten;");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS dbo.fn_zeltlotse_mandanten;");
    }
}
