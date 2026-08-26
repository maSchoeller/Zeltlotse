using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Zeltlotse.Core.Persistenz;

/// <summary>
/// Setzt auf jeder geöffneten Verbindung die Rolle des Anwendungskontos und die
/// Liste der Organisationen, die der angemeldete Nutzer sehen darf. Beides
/// zusammen aktiviert die Row-Level-Security in PostgreSQL.
///
/// Die Liste stammt aus dem Zugriffstoken, nicht aus einer Abfrage — eine
/// Abfrage bräuchte die Verbindung, die hier gerade erst geöffnet wird. Der
/// Preis ist eine Nachlaufzeit von höchstens der Tokenlebensdauer (15 Minuten);
/// die genaue Rechteprüfung im Anwendungscode liest weiterhin aus der Datenbank.
/// </summary>
public sealed class MandantInterceptor(IMandantKontext mandant) : DbConnectionInterceptor
{
    /// <summary>Nicht-Superuser — nur so greifen die Richtlinien überhaupt.</summary>
    public const string Anwendungsrolle = "zeltlotse_app";

    public override async Task ConnectionOpenedAsync(
        DbConnection verbindung,
        ConnectionEndEventData daten,
        CancellationToken abbruch = default)
    {
        if (mandant.Wartung || verbindung is not NpgsqlConnection npgsql)
        {
            return;
        }

        // Zwei getrennte Befehle: SET ROLE kennt keine Parameter, und ein
        // gemischter Stapel aus parameterlosem und parametrisiertem Statement
        // ist im erweiterten Protokoll nicht zulässig.
        await using (var rolle = npgsql.CreateCommand())
        {
            rolle.CommandText = $"SET ROLE {Anwendungsrolle}";
            await rolle.ExecuteNonQueryAsync(abbruch);
        }

        await using var mandanten = npgsql.CreateCommand();
        mandanten.CommandText = "SELECT set_config('app.tenant_ids', $1, false)";
        mandanten.Parameters.Add(new NpgsqlParameter
        {
            Value = string.Join(',', mandant.SichtbareOrganisationen),
        });

        await mandanten.ExecuteNonQueryAsync(abbruch);

        await using var betreiber = npgsql.CreateCommand();
        betreiber.CommandText = "SELECT set_config('app.betreiber', $1, false)";
        betreiber.Parameters.Add(new NpgsqlParameter { Value = mandant.IstBetreiber ? "1" : "" });

        await betreiber.ExecuteNonQueryAsync(abbruch);
    }
}
