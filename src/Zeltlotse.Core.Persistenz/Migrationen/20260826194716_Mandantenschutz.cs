using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Core.Persistenz.Migrationen;

/// <summary>
/// Das zweite Netz der Mandantentrennung: Row-Level-Security in PostgreSQL.
///
/// Wirksam wird sie nur, weil die Anwendung ihre Verbindungen per SET ROLE auf
/// ein Konto ohne Superuser-Rechte umstellt — ein Superuser umgeht jede
/// Richtlinie, und der Eigentümer einer Tabelle ebenfalls, solange nicht FORCE
/// gesetzt ist. Beides ist hier berücksichtigt.
/// </summary>
public partial class Mandantenschutz : Migration
{
    private static readonly string[] GeschuetzteTabellen =
        ["freizeit", "freizeit_zuordnung", "einladung"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'zeltlotse_app') THEN
                    CREATE ROLE zeltlotse_app NOLOGIN;
                END IF;
            END
            $$;
            """);

        migrationBuilder.Sql("""
            GRANT USAGE ON SCHEMA public TO zeltlotse_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO zeltlotse_app;
            GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO zeltlotse_app;
            ALTER DEFAULT PRIVILEGES IN SCHEMA public
                GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO zeltlotse_app;
            """);

        // Liest die Organisationen des angemeldeten Nutzers aus der
        // Sitzungsvariablen. Leer oder nicht gesetzt bedeutet: keine Zeile.
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION zeltlotse_mandanten() RETURNS uuid[] AS $$
                SELECT CASE
                    WHEN coalesce(current_setting('app.tenant_ids', true), '') = ''
                        THEN ARRAY[]::uuid[]
                    ELSE string_to_array(current_setting('app.tenant_ids', true), ',')::uuid[]
                END
            $$ LANGUAGE sql STABLE;
            """);

        foreach (var tabelle in GeschuetzteTabellen)
        {
            migrationBuilder.Sql($"""
                ALTER TABLE {tabelle} ENABLE ROW LEVEL SECURITY;
                ALTER TABLE {tabelle} FORCE ROW LEVEL SECURITY;
                CREATE POLICY {tabelle}_mandant ON {tabelle}
                    USING ("TenantId" = ANY (zeltlotse_mandanten()))
                    WITH CHECK ("TenantId" = ANY (zeltlotse_mandanten()));
                """);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var tabelle in GeschuetzteTabellen)
        {
            migrationBuilder.Sql($"""
                DROP POLICY IF EXISTS {tabelle}_mandant ON {tabelle};
                ALTER TABLE {tabelle} NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE {tabelle} DISABLE ROW LEVEL SECURITY;
                """);
        }

        migrationBuilder.Sql("DROP FUNCTION IF EXISTS zeltlotse_mandanten();");
    }
}
