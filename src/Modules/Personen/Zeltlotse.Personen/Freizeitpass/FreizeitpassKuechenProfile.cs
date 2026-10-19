using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Personen.Contracts.Freizeitpass;
using Zeltlotse.Personen.Daten;
using Zeltlotse.Personen.Freigaben;
using Zeltlotse.Personen.Zugriffsprotokoll;
using Zeltlotse.SharedKernel.Datenschutz;

namespace Zeltlotse.Personen.Freizeitpass;

/// <summary>
/// Die Lesepfade ohne Menschen für die Küche. Story 4-2: <see cref="IFreizeitpassAllergene"/>, der zweckgebundene Lesepfad „Allergene für die Küche“ (AD-8, AD-10, FR-48). Story 4-4: dazu
/// <see cref="IFreizeitpassSonderkost"/>, „Sonderkost für die Küche“ (FR-52). Beide teilen <see cref="AuswahlProfile"/> und unterscheiden sich nur in der
/// Kennung der Frage und im Zweck des Protokolls.
/// </summary>
internal sealed class FreizeitpassKuechenProfile(AuswahlProfile profile) : IFreizeitpassAllergene, IFreizeitpassSonderkost
{
    Task<IReadOnlyList<IReadOnlyList<string>>> IFreizeitpassAllergene.ProfileAsync(Guid freizeitId, IReadOnlyCollection<Guid> personIds, CancellationToken abbruch) =>
        profile.LesenAsync(Fragenkatalog.Allergien, EinsichtZweck.AllergeneFuerKueche, freizeitId, personIds, abbruch);

    Task<IReadOnlyList<IReadOnlyList<string>>> IFreizeitpassSonderkost.ProfileAsync(Guid freizeitId, IReadOnlyCollection<Guid> personIds, CancellationToken abbruch) =>
        profile.LesenAsync(Fragenkatalog.Ernaehrung, EinsichtZweck.SonderkostFuerKueche, freizeitId, personIds, abbruch);
}

/// <summary>
/// Der gemeinsame Kern der Lesepfade ohne Menschen für die Küche (AD-8, AD-10): anonyme Profile einer Auswahl-Frage. Je übergebener Kennung zählt die
/// jüngste Freigabe dieser Freizeit (wie bei Einsicht und Notfallordner); aufgenommen wird nur, wer bestätigt ist (Stufe Team), dessen Freigabe noch gilt
/// und die Frage enthält. Gelesen wird nur die Auswahl, nie „Sonstiges“; ist der Schlüssel der Person vernichtet, fällt sie still weg. Erst wenn alle
/// Profile stehen, schreibt der Lesepfad je gelesener Person einen Eintrag ins Einsicht-Protokoll (der übergebene Zweck, ohne lesende Person, höchstens
/// einmal am Tag).
/// </summary>
internal sealed class AuswahlProfile(PersonenLeseDbContext db, IVerschluesselung verschluesselung, ISender sender, TimeProvider uhr)
{
    public async Task<IReadOnlyList<IReadOnlyList<string>>> LesenAsync(
        string kennung, EinsichtZweck zweck, Guid freizeitId, IReadOnlyCollection<Guid> personIds, CancellationToken abbruch)
    {
        var frage = Fragenkatalog.Finden(kennung) ?? throw new ArgumentException($"Unbekannte Frage: {kennung}.", nameof(kennung));
        var kennungen = personIds.Distinct().ToList();
        if (kennungen.Count == 0)
        {
            return [];
        }

        var jetzt = uhr.GetUtcNow();
        var freigaben = (await FreizeitpassZugriff.JuengsteFreigabenAsync(db, freizeitId, kennungen, abbruch)).Values
            .Where(f => f.Stufe == FreigabeStufe.Team && f.GueltigBis > jetzt && f.Fragen.Contains(kennung))
            .ToDictionary(f => f.PersonId);
        var ids = freigaben.Keys.ToList();
        var antworten = await db.FreizeitpassAntworten
            .Where(a => ids.Contains(a.PersonId) && a.Kennung == kennung && a.Wert != null)
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
            var auswahl = Fragenkatalog.WertPruefen(frage, wert);
            var schluessel = auswahl.Gueltig && auswahl.Wert is { } liste ? liste.Split(',') : [];
            if (schluessel.Length > 0)
            {
                profile.Add(schluessel);
            }
        }

        foreach (var freigabe in gelesen)
        {
            await sender.Send(new EinsichtProtokollieren(freigabe.PersonId, null, freigabe.OrganisationId, freizeitId, zweck), abbruch);
        }

        return [.. profile.OrderBy(p => string.Join(',', p), StringComparer.Ordinal)];
    }
}
