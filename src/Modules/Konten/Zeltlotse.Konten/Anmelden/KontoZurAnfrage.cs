using Microsoft.EntityFrameworkCore;
using Zeltlotse.Konten.Domaene;
using Zeltlotse.Konten.Sitzungen;
using Zeltlotse.SharedKernel;

namespace Zeltlotse.Konten.Anmelden;

/// <summary>
/// Gemeinsamer Schritt von Link und Code: Die Anfrage ist eingelöst, der Besitz der Mail-Adresse belegt.
/// Gibt es zur Mail-Adresse noch kein Konto, entsteht es hier, mit neuer <c>PersonId</c>. Bekommt nur die Konten-Tabelle
/// des Handlers, nicht den schreibenden DbContext (AD-27).
/// </summary>
internal static class KontoZurAnfrage
{
    public static async Task<SitzungsDaten> EinloesenAsync(DbSet<Konto> konten, LoginAnfrage anfrage, TimeProvider uhr, CancellationToken abbruch)
    {
        var jetzt = uhr.GetUtcNow();
        anfrage.Einloesen(jetzt);
        var konto = await konten.SingleOrDefaultAsync(k => k.Mail == anfrage.Mail, abbruch);
        if (konto is null)
        {
            konto = Konto.Anlegen(Kennung.Neu(uhr), anfrage.Mail, Kennung.Neu(uhr), jetzt);
            konten.Add(konto);
        }

        return new SitzungsDaten(konto.Id, konto.PersonId, konto.SitzungsStempel);
    }
}
