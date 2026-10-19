using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Personen.Contracts.Freizeitpass;
using Zeltlotse.Personen.Daten;
using Zeltlotse.Personen.Freigaben;
using Zeltlotse.Personen.Zugriffsprotokoll;
using Zeltlotse.SharedKernel.Datenschutz;

namespace Zeltlotse.Personen.Freizeitpass;

/// <summary>
/// Story 4-2: <see cref="IFreizeitpassAllergene"/>, der zweckgebundene Lesepfad „Allergene für die Küche“ (AD-8, AD-10, FR-48). Je übergebener
/// Kennung zählt die jüngste Freigabe dieser Freizeit (wie bei Einsicht und Notfallordner); aufgenommen wird nur, wer bestätigt ist (Stufe Team),
/// dessen Freigabe noch gilt und die Allergie-Frage enthält. Gelesen wird nur die Auswahl, nie „Sonstiges“; ist der Schlüssel der Person
/// vernichtet, fällt sie still weg. Erst wenn alle Profile stehen, schreibt der Lesepfad je gelesener Person einen Eintrag ins Einsicht-Protokoll
/// (Zweck <see cref="EinsichtZweck.AllergeneFuerKueche"/>, ohne lesende Person, höchstens einmal am Tag).
/// </summary>
internal sealed class FreizeitpassAllergene(PersonenLeseDbContext db, IVerschluesselung verschluesselung, ISender sender, TimeProvider uhr)
    : IFreizeitpassAllergene
{
    private static readonly Katalogfrage Frage = Fragenkatalog.Finden(Fragenkatalog.Allergien)!;

    public async Task<IReadOnlyList<IReadOnlyList<string>>> ProfileAsync(Guid freizeitId, IReadOnlyCollection<Guid> personIds, CancellationToken abbruch = default)
    {
        var kennungen = personIds.Distinct().ToList();
        if (kennungen.Count == 0)
        {
            return [];
        }

        var jetzt = uhr.GetUtcNow();
        var freigaben = (await FreizeitpassZugriff.JuengsteFreigabenAsync(db, freizeitId, kennungen, abbruch)).Values
            .Where(f => f.Stufe == FreigabeStufe.Team && f.GueltigBis > jetzt && f.Fragen.Contains(Fragenkatalog.Allergien))
            .ToDictionary(f => f.PersonId);
        var ids = freigaben.Keys.ToList();
        var antworten = await db.FreizeitpassAntworten
            .Where(a => ids.Contains(a.PersonId) && a.Kennung == Fragenkatalog.Allergien && a.Wert != null)
            .ToListAsync(abbruch);

        var profile = new List<IReadOnlyList<string>>();
        var gelesen = new List<Freigabe>();
        foreach (var antwort in antworten)
        {
            string wert;
            try
            {
                wert = await verschluesselung.EntschluesselnAsync(antwort.Wert!, abbruch);
            }
            catch (PersonenSchluesselFehltAusnahme)
            {
                continue;
            }

            gelesen.Add(freigaben[antwort.PersonId]);
            var auswahl = Fragenkatalog.WertPruefen(Frage, wert);
            var schluessel = auswahl.Gueltig && auswahl.Wert is { } liste ? liste.Split(',') : [];
            if (schluessel.Length > 0)
            {
                profile.Add(schluessel);
            }
        }

        foreach (var freigabe in gelesen)
        {
            await sender.Send(new EinsichtProtokollieren(freigabe.PersonId, null, freigabe.OrganisationId, freizeitId, EinsichtZweck.AllergeneFuerKueche), abbruch);
        }

        return [.. profile.OrderBy(p => string.Join(',', p), StringComparer.Ordinal)];
    }
}
