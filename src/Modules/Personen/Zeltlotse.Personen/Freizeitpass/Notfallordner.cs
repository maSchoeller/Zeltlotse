using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Personen.Contracts.Freizeitpass;
using Zeltlotse.Personen.Daten;
using Zeltlotse.Personen.Freigaben;
using Zeltlotse.Personen.Zugriffsprotokoll;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Datenschutz;

namespace Zeltlotse.Personen.Freizeitpass;

/// <summary>
/// <c>GET /api/freizeiten/{freizeitId}/personen/notfallordner?person={id}&amp;person={id}…</c> (Leitung und Team, Story 2-11, FR-33, AD-8):
/// die Freizeitpässe einer Menge von Personen für den Druck des Notfallordners. Die Kennungen liefert der Client (bestätigte Teilnehmende
/// aus der Personenliste von Freizeiten); Personen prüft selbst: Je Kennung zählt die jüngste Freigabe dieser Freizeit, aufgenommen wird
/// nur, wer bestätigt ist (Stufe Team) und dessen Freigabe noch gilt. Alle anderen Kennungen fallen still weg. Je aufgenommener Person ein
/// Eintrag im Einsicht-Protokoll (Zweck „Freizeitpass eingesehen“, höchstens einmal am Tag), nicht für die lesende Person selbst. Rolle
/// und jüngste Freigabe teilt der Endpunkt mit der Einsicht (<see cref="FreizeitpassZugriff"/>). Ohne Kennung oder mit mehr als
/// <see cref="NotfallordnerAbfrage.HoechstensKennungen"/> → <c>400</c>; ohne Rolle in der Rollenliste von Personen → <c>404</c>.
/// </summary>
internal static class NotfallordnerEndpunkt
{
    public static async Task<Results<Ok<List<NotfallordnerPerson>>, NotFound>> Laden(
        Guid freizeitId, [FromQuery(Name = NotfallordnerAbfrage.Parameter)] Guid[]? person, IAngemeldet angemeldet, PersonenLeseDbContext db,
        IVerschluesselung verschluesselung, ISender sender, TimeProvider uhr, CancellationToken abbruch)
    {
        var kennungen = (person ?? []).Distinct().ToList();
        if (kennungen.Count == 0 || kennungen.Count > NotfallordnerAbfrage.HoechstensKennungen)
        {
            throw new ValidierungsAusnahme(new Dictionary<string, string[]>
            {
                [NotfallordnerAbfrage.Parameter] = [$"Mindestens eine und höchstens {NotfallordnerAbfrage.HoechstensKennungen} Kennungen."],
            });
        }

        var ich = FreizeitpassAbfrage.PersonId(angemeldet);
        var eigene = await FreizeitpassZugriff.RollenAsync(db, freizeitId, ich, abbruch);
        if (eigene.Count == 0)
        {
            return TypedResults.NotFound();
        }

        var jetzt = uhr.GetUtcNow();
        var bestaetigt = (await FreizeitpassZugriff.JuengsteFreigabenAsync(db, freizeitId, kennungen, abbruch)).Values
            .Where(f => f.Stufe == FreigabeStufe.Team && FreizeitpassZugriff.Zustand(f, eigene, jetzt) == FreizeitpassEinsichtZustand.Sichtbar)
            .ToDictionary(f => f.PersonId);
        var ids = bestaetigt.Keys.ToList();
        var namen = await db.Personen.Where(p => ids.Contains(p.Id)).Select(p => new { p.Id, p.Vorname, p.Nachname }).ToListAsync(abbruch);

        var ordner = new List<NotfallordnerPerson>();
        foreach (var name in namen
                     .OrderBy(p => p.Nachname ?? "", StringComparer.CurrentCulture)
                     .ThenBy(p => p.Vorname ?? "", StringComparer.CurrentCulture)
                     .ThenBy(p => p.Id))
        {
            var freigabe = bestaetigt[name.Id];
            var freigegeben = freigabe.Fragen.ToHashSet(StringComparer.Ordinal);
            var fragen = await FreizeitpassAbfrage.FragenAsync(
                db, verschluesselung, name.Id, Fragenkatalog.Fragen.Where(f => freigegeben.Contains(f.Kennung)), jetzt, abbruch);
            ordner.Add(new NotfallordnerPerson(name.Id, name.Vorname ?? "", name.Nachname ?? "", fragen));
        }

        // Erst wenn alle Pässe zusammengestellt sind, ins Einsicht-Protokoll: kein Eintrag für Daten, die nie ausgeliefert wurden.
        foreach (var eintrag in ordner.Where(p => p.PersonId != ich))
        {
            await sender.Send(
                new EinsichtProtokollieren(
                    eintrag.PersonId, ich, bestaetigt[eintrag.PersonId].OrganisationId, freizeitId, EinsichtZweck.FreizeitpassEingesehen),
                abbruch);
        }

        return TypedResults.Ok(ordner);
    }
}
