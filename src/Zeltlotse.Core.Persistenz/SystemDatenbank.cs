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
    public ZeltlotseDbContext Oeffnen()
    {
        var optionen = new DbContextOptionsBuilder<ZeltlotseDbContext>()
            .UseNpgsql(verbindungszeichenfolge)
            .Options;

        // Kein Interceptor: keine Rollenumstellung, keine Sitzungsvariablen.
        return new ZeltlotseDbContext(optionen, new MandantKontext { Wartung = true });
    }
}
