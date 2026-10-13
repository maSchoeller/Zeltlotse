using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Personen;
using FreizeitRolleArt = Zeltlotse.Freizeiten.Contracts.Events.FreizeitRolleArt;
using Zeltlotse.Personen.Contracts.Freizeitpass;
using Zeltlotse.Personen.Daten;
using Zeltlotse.Personen.Freigaben;
using Zeltlotse.Personen.Zugriffsprotokoll;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Datenschutz;
using RolleArt = Zeltlotse.SharedKernel.Rechte.FreizeitRolle;

namespace Zeltlotse.Personen.Freizeitpass;

/// <summary>
/// Rollenliste der Einsicht in Freizeitpässe im Freizeit-Kontext (Story 2-5, FR-24, AD-12). Die erlaubte Aktion
/// <c>freizeitpaesse-sehen</c> berechnet Freizeiten aus derselben Liste (<c>FreizeitRechte.Freizeitpaesse</c>); ein Test hält beide gleich.
/// </summary>
internal static class FreizeitpassRechte
{
    /// <summary>Leitung und Team; was davon sichtbar ist, entscheidet die Freigabe.</summary>
    public static readonly RolleArt[] Einsicht = [RolleArt.Leitung, RolleArt.Team];

    /// <summary>
    /// Story 2-6: Freizeitpass-Stand sehen und zum Freizeitpass erinnern (nur Leitung, FR-25). In der Oberfläche gilt die erlaubte Aktion
    /// <c>anmeldungen-bearbeiten</c> aus <c>FreizeitRechte.Anmeldungen</c>; ein Test hält beide Listen gleich.
    /// </summary>
    public static readonly RolleArt[] Erinnern = [RolleArt.Leitung];
}

/// <summary>
/// <c>GET /api/freizeiten/{freizeitId}/personen/freizeitpass/{personId}</c> (Leitung und Team, Story 2-5, FR-24, AD-8): der Freizeitpass
/// einer Person im Kontext der Freizeit. Die Rolle der lesenden Person kommt nur aus der eigenen Rollenliste von Personen, nie vom
/// Aufrufer; ohne Zeile dort oder ohne Freigabe der Person für diese Freizeit <c>404</c>. Maßgeblich ist die Freigabe mit dem jüngsten
/// „angelegt am“: abgelaufen oder abgesagt → <c>beendet</c>; Stufe Leitung und die lesende Person ist nur Team → <c>abBestaetigung</c>;
/// sonst <c>sichtbar</c> mit genau den Fragen der Freigabe. Nur bei <c>sichtbar</c> kommt ein Eintrag ins Einsicht-Protokoll der
/// betroffenen Person (höchstens einmal am Tag), nicht beim Blick auf den eigenen Pass. Team-Notizen stehen hier nie.
/// </summary>
internal static class FreizeitpassEinsichtEndpunkt
{
    public static async Task<Results<Ok<FreizeitpassEinsicht>, NotFound>> Laden(
        Guid freizeitId, Guid personId, IAngemeldet angemeldet, PersonenLeseDbContext db, IVerschluesselung verschluesselung, ISender sender,
        TimeProvider uhr, CancellationToken abbruch)
    {
        var ich = FreizeitpassAbfrage.PersonId(angemeldet);
        var eigene = await FreizeitpassZugriff.RollenAsync(db, freizeitId, ich, abbruch);
        var freigabe = eigene.Count == 0
            ? null
            : (await FreizeitpassZugriff.JuengsteFreigabenAsync(db, freizeitId, [personId], abbruch)).GetValueOrDefault(personId);
        var person = freigabe is null
            ? null
            : await db.Personen.Where(p => p.Id == personId).Select(p => new { p.Vorname, p.Nachname }).SingleOrDefaultAsync(abbruch);
        if (freigabe is null || person is null)
        {
            return TypedResults.NotFound();
        }

        var jetzt = uhr.GetUtcNow();
        var zustand = FreizeitpassZugriff.Zustand(freigabe, eigene, jetzt);
        List<FreizeitpassFrage> fragen = [];
        if (zustand == FreizeitpassEinsichtZustand.Sichtbar)
        {
            var freigegeben = freigabe.Fragen.ToHashSet(StringComparer.Ordinal);
            fragen = await FreizeitpassAbfrage.FragenAsync(
                db, verschluesselung, personId, Fragenkatalog.Fragen.Where(f => freigegeben.Contains(f.Kennung)), jetzt, abbruch);
            if (ich != personId)
            {
                await sender.Send(
                    new EinsichtProtokollieren(personId, ich, freigabe.OrganisationId, freizeitId, EinsichtZweck.FreizeitpassEingesehen), abbruch);
            }
        }

        return TypedResults.Ok(new FreizeitpassEinsicht(
            personId,
            person.Vorname,
            person.Nachname,
            zustand,
            freigabe.GueltigBis,
            zustand == FreizeitpassEinsichtZustand.Beendet ? freigabe.GueltigBis : null,
            fragen));
    }
}

/// <summary>
/// Die Rechte-Logik der Einsicht in Freizeitpässe im Freizeit-Kontext, an genau einer Stelle (Story 2-5, Story 2-11, AD-8): die eigenen
/// Rollen der lesenden Person aus der Rollenliste von Personen (nie vom Aufrufer), je Person die jüngste Freigabe dieser Freizeit
/// („angelegt am“, dann Kennung absteigend) und der Zustand, den die lesende Person davon sieht. Genutzt von
/// <see cref="FreizeitpassEinsichtEndpunkt"/> und <see cref="NotfallordnerEndpunkt"/>.
/// </summary>
internal static class FreizeitpassZugriff
{
    /// <summary>Die Rollen der lesenden Person in dieser Freizeit, soweit sie Einsicht geben (<see cref="FreizeitpassRechte.Einsicht"/>).</summary>
    public static async Task<List<RolleArt>> RollenAsync(PersonenLeseDbContext db, Guid freizeitId, Guid ich, CancellationToken abbruch)
    {
        var rollen = await db.FreizeitRollen.Where(r => r.FreizeitId == freizeitId && r.PersonId == ich).Select(r => r.Rolle).ToListAsync(abbruch);
        return [.. rollen.Select(Rolle).OfType<RolleArt>().Where(FreizeitpassRechte.Einsicht.Contains)];
    }

    /// <summary>Je Person die maßgebliche, jüngste Freigabe für diese Freizeit; Personen ohne Freigabe fehlen.</summary>
    public static async Task<Dictionary<Guid, Freigabe>> JuengsteFreigabenAsync(
        PersonenLeseDbContext db, Guid freizeitId, IReadOnlyCollection<Guid> personen, CancellationToken abbruch)
    {
        var freigaben = await db.Freigaben.Where(f => f.FreizeitId == freizeitId && personen.Contains(f.PersonId)).ToListAsync(abbruch);
        return freigaben
            .GroupBy(f => f.PersonId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(f => f.AngelegtAm).ThenByDescending(f => f.Id).First());
    }

    /// <summary>
    /// Abgelaufen oder abgesagt → <c>beendet</c>; Stufe Leitung und die lesende Person ist nur Team → <c>abBestaetigung</c>; sonst
    /// <c>sichtbar</c>.
    /// </summary>
    public static FreizeitpassEinsichtZustand Zustand(Freigabe freigabe, IReadOnlyCollection<RolleArt> eigene, DateTimeOffset jetzt) =>
        freigabe.GueltigBis <= jetzt
            ? FreizeitpassEinsichtZustand.Beendet
            : freigabe.Stufe == FreigabeStufe.Leitung && !eigene.Contains(RolleArt.Leitung)
                ? FreizeitpassEinsichtZustand.AbBestaetigung
                : FreizeitpassEinsichtZustand.Sichtbar;

    private static RolleArt? Rolle(FreizeitRolleArt rolle) => rolle switch
    {
        FreizeitRolleArt.Leitung => RolleArt.Leitung,
        FreizeitRolleArt.Team => RolleArt.Team,
        _ => null,
    };
}
