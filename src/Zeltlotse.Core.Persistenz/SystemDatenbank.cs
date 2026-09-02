using Microsoft.EntityFrameworkCore;

namespace Zeltlotse.Core.Persistenz;

/// <summary>
/// Eine eigene, kurzlebige Verbindung ohne Mandantenschranke — für die wenigen
/// Zugriffe, die sie nachweislich nicht anwenden können.
///
/// Es gibt genau einen solchen Fall: Eine Einladung wird eingelöst, bevor
/// jemand angemeldet ist. Ohne Anmeldung gibt es keine Organisationsliste, aus
/// der sich die Row-Level-Security speisen könnte, und die Datenbank verweigert
/// selbst die Einladung, die geprüft werden soll.
///
/// Bewusst als eigener Kontext statt als Schalter an der laufenden Anfrage: Ein
/// Schalter bliebe für alles Weitere umgelegt und wäre für einen späteren Lauf
/// unsichtbar. So steht die Ausnahme an genau einer Stelle und endet mit der
/// Verwendung.
/// </summary>
public sealed class SystemDatenbank(string verbindungszeichenfolge)
{
    /// <summary>
    /// Kein Interceptor — die Ausnahme wird hier direkt und einmalig gesetzt,
    /// statt über <see cref="MandantInterceptor"/>. Anders als PostgreSQL
    /// kennt SQL Server keinen automatischen Bypass für privilegierte
    /// Verbindungen; die Richtlinie muss die Ausnahme deshalb explizit über
    /// dieselbe Sitzungsvariable erkennen, mit der auch die
    /// Betreiber-Ausnahme arbeitet.
    /// </summary>
    public async Task<ZeltlotseDbContext> OeffnenAsync()
    {
        var optionen = new DbContextOptionsBuilder<ZeltlotseDbContext>()
            .UseSqlServer(verbindungszeichenfolge)
            .Options;

        var kontext = new ZeltlotseDbContext(optionen, new MandantKontext { Wartung = true });

        var verbindung = kontext.Database.GetDbConnection();
        await verbindung.OpenAsync();

        await using var befehl = verbindung.CreateCommand();
        befehl.CommandText = "EXEC sp_set_session_context @key = N'system_bypass', @value = 1, @read_only = 1";
        await befehl.ExecuteNonQueryAsync();

        return kontext;
    }
}
