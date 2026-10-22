using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Personen.Contracts.Freizeitpass;
using Zeltlotse.Personen.Daten;
using Zeltlotse.Personen.Freigaben;
using Zeltlotse.Personen.Zugriffsprotokoll;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Datenschutz;

namespace Zeltlotse.Personen.Freizeitpass;

/// <summary>
/// <c>GET /api/freizeiten/{freizeitId}/personen/notfallkarte</c> (Leitung und Team, Story 6-5, FR-65, AD-8, AD-18): die Notfallangaben
/// aller Personen dieser Freizeit für den verschlüsselten Offline-Speicher der Web-App. Die Personen bestimmt Personen selbst: Je Person zählt
/// die jüngste Freigabe dieser Freizeit, aufgenommen wird nur, wer bestätigt ist (Stufe Team) und dessen Freigabe noch gilt. Je Person nur die
/// nötigen Felder (AD-18): der Schnitt aus der Freigabe und <see cref="NotfallkarteEndpunkt.Kennungen"/>. Je aufgenommener Person ein Eintrag
/// im Einsicht-Protokoll (Zweck „Notfallkarte aufs Gerät geladen“, höchstens einmal am Tag), nicht für die lesende Person selbst. Ohne Rolle
/// in der Rollenliste von Personen → <c>404</c>.
/// </summary>
internal static class NotfallkarteEndpunkt
{
    /// <summary>Die notfallrelevanten Fragen (CAP-15 plus Krankheiten).</summary>
    public static readonly IReadOnlySet<string> Kennungen = new HashSet<string>(StringComparer.Ordinal)
    {
        Fragenkatalog.Allergien, Fragenkatalog.Medikamente, Fragenkatalog.Krankheiten, Fragenkatalog.Notfallkontakte,
    };

    public static async Task<Results<Ok<List<NotfallkartePerson>>, NotFound>> Laden(
        Guid freizeitId, IAngemeldet angemeldet, PersonenLeseDbContext db, IVerschluesselung verschluesselung, ISender sender, TimeProvider uhr,
        CancellationToken abbruch)
    {
        var ich = FreizeitpassAbfrage.PersonId(angemeldet);
        var eigene = await FreizeitpassZugriff.RollenAsync(db, freizeitId, ich, abbruch);
        if (eigene.Count == 0)
        {
            return TypedResults.NotFound();
        }

        var jetzt = uhr.GetUtcNow();
        var personen = await db.Freigaben.Where(f => f.FreizeitId == freizeitId).Select(f => f.PersonId).Distinct().ToListAsync(abbruch);
        var bestaetigt = (await FreizeitpassZugriff.JuengsteFreigabenAsync(db, freizeitId, personen, abbruch)).Values
            .Where(f => f.Stufe == FreigabeStufe.Team && FreizeitpassZugriff.Zustand(f, eigene, jetzt) == FreizeitpassEinsichtZustand.Sichtbar)
            .ToDictionary(f => f.PersonId);
        var ids = bestaetigt.Keys.ToList();
        var namen = await db.Personen.Where(p => ids.Contains(p.Id)).Select(p => new { p.Id, p.Vorname, p.Nachname }).ToListAsync(abbruch);

        var karte = new List<NotfallkartePerson>();
        foreach (var name in namen
                     .OrderBy(p => p.Nachname ?? "", StringComparer.CurrentCulture)
                     .ThenBy(p => p.Vorname ?? "", StringComparer.CurrentCulture)
                     .ThenBy(p => p.Id))
        {
            var freigegeben = bestaetigt[name.Id].Fragen.Where(Kennungen.Contains).ToHashSet(StringComparer.Ordinal);
            var fragen = await FreizeitpassAbfrage.FragenAsync(
                db, verschluesselung, name.Id, Fragenkatalog.Fragen.Where(f => freigegeben.Contains(f.Kennung)), jetzt, abbruch);
            karte.Add(new NotfallkartePerson(name.Id, name.Vorname ?? "", name.Nachname ?? "", fragen));
        }

        // Erst wenn die Karte zusammengestellt ist, ins Einsicht-Protokoll: kein Eintrag für Daten, die nie ausgeliefert wurden.
        foreach (var eintrag in karte.Where(p => p.PersonId != ich))
        {
            await sender.Send(
                new EinsichtProtokollieren(
                    eintrag.PersonId, ich, bestaetigt[eintrag.PersonId].OrganisationId, freizeitId, EinsichtZweck.NotfallkarteGeladen),
                abbruch);
        }

        return TypedResults.Ok(karte);
    }
}
