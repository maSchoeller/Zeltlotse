using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Zeltlotse.Core.Persistenz;

/// <summary>
/// Setzt auf jeder geöffneten Verbindung die Liste der Organisationen, die der
/// angemeldete Nutzer sehen darf, als Sitzungsvariable. Das aktiviert die
/// Row-Level-Security in Azure SQL — anders als PostgreSQL kennt SQL Server
/// keine Rollenumstellung für diesen Zweck, die Sitzungsvariable allein
/// steuert die Prädikatfunktion der Sicherheitsrichtlinie.
///
/// Die Liste stammt aus dem Zugriffstoken, nicht aus einer Abfrage — eine
/// Abfrage bräuchte die Verbindung, die hier gerade erst geöffnet wird. Der
/// Preis ist eine Nachlaufzeit von höchstens der Tokenlebensdauer (15 Minuten);
/// die genaue Rechteprüfung im Anwendungscode liest weiterhin aus der Datenbank.
///
/// <c>@read_only = 1</c> sperrt den Wert bis die Verbindung geschlossen bzw.
/// an den Pool zurückgegeben wird — genau der Zeitpunkt, zu dem diese Methode
/// beim nächsten logischen Öffnen erneut aufgerufen wird.
///
/// Im Wartungsmodus (Migrationen, Aufräumdienst) setzt diese Methode
/// stattdessen dieselbe <c>system_bypass</c>-Sitzungsvariable wie
/// <see cref="SystemDatenbank"/>: Anders als in PostgreSQL, wo dafür die
/// unveränderte, privilegierte Basisrolle der Verbindung ausreichte, kennt
/// SQL Server unter Azure-AD-Auth keine solche privilegierte Rolle — jede
/// Verbindung läuft unter derselben, eingeschränkten Identität.
/// </summary>
public sealed class MandantInterceptor(IMandantKontext mandant) : DbConnectionInterceptor
{
    public override async Task ConnectionOpenedAsync(
        DbConnection verbindung,
        ConnectionEndEventData daten,
        CancellationToken abbruch = default)
    {
        if (verbindung is not SqlConnection sql)
        {
            return;
        }

        if (mandant.Wartung)
        {
            await using var bypass = sql.CreateCommand();
            bypass.CommandText =
                "EXEC sp_set_session_context @key = N'system_bypass', @value = 1, @read_only = 1";

            await bypass.ExecuteNonQueryAsync(abbruch);
            return;
        }

        await using var mandanten = sql.CreateCommand();
        mandanten.CommandText =
            "EXEC sp_set_session_context @key = N'tenant_ids', @value = @wert, @read_only = 1";
        mandanten.Parameters.Add(new SqlParameter("@wert", string.Join(',', mandant.SichtbareOrganisationen)));

        await mandanten.ExecuteNonQueryAsync(abbruch);

        await using var betreiber = sql.CreateCommand();
        betreiber.CommandText =
            "EXEC sp_set_session_context @key = N'betreiber', @value = @wert, @read_only = 1";
        betreiber.Parameters.Add(new SqlParameter("@wert", mandant.IstBetreiber ? "1" : ""));

        await betreiber.ExecuteNonQueryAsync(abbruch);
    }
}
