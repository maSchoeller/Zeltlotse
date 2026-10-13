using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Freizeiten.Daten;
using Zeltlotse.Freizeiten.Dokumente;
using Zeltlotse.Freizeiten.Domaene;
using Zeltlotse.Freizeiten.Rechte;
using Zeltlotse.Organisationen.Contracts.Dokumente;
using Zeltlotse.Organisationen.Contracts.Vertrauensseite;
using Zeltlotse.Personen.Contracts.Freizeitpass;
using Zeltlotse.Personen.Contracts.Namen;
using Zeltlotse.SharedKernel.Mandanten;
using ApiFreizeitseite = Zeltlotse.Api.Contracts.Freizeiten.Freizeitseite;
using OrgNachweisArt = Zeltlotse.Organisationen.Contracts.Nachweise.NachweisArt;

namespace Zeltlotse.Freizeiten.Freizeitseite;

/// <summary>
/// Bestätigte Teilnehmende einer Freizeit für die Platzlage (FR-67); umgesetzt von
/// <c>Anmeldungen.Entscheiden.BestaetigteTeilnehmendeZaehlen</c> (Story 2-3).
/// </summary>
internal interface IBestaetigteTeilnehmende
{
    Task<int> AnzahlAsync(Guid freizeitId, CancellationToken abbruch = default);
}

/// <summary>
/// <c>GET /api/freizeiten/oeffentlich/{token}</c> (anonym, zugriffsbegrenzt, AD-11): die öffentliche Freizeitseite (FR-15, FR-67).
/// Mandant aus dem Token (<see cref="AnmeldelinkSuche"/>). Vom Team nur Vorname, Initialen, Rolle und Herkunft; Personen ohne
/// Vorname fehlen. Ungültiger Link <c>404</c>.
/// </summary>
internal static class FreizeitseiteOeffentlichEndpunkt
{
    public static async Task<Results<Ok<ApiFreizeitseite>, NotFound>> Laden(
        string token,
        FreizeitenLeseDbContext db,
        MandantenKontext mandant,
        IVertrauensAngabenLesen organisation,
        IPersonenSteckbriefe steckbriefe,
        IBestaetigteTeilnehmende bestaetigte,
        IOrgDokumenteLesen orgDokumente,
        TimeProvider uhr,
        CancellationToken abbruch)
    {
        if (await AnmeldelinkSuche.FindenAsync(db, mandant, token, uhr.GetUtcNow(), abbruch) is not { } gefunden
            || await organisation.LesenAsync(abbruch) is not { } angaben)
        {
            return TypedResults.NotFound();
        }

        var (link, freizeit) = gefunden;

        var rollen = await db.Rollen.Where(r => r.FreizeitId == freizeit.Id).Select(r => new { r.PersonId, r.Rolle }).ToListAsync(abbruch);
        var personen = await steckbriefe.SteckbriefeAsync([.. rollen.Select(r => r.PersonId).Distinct()], abbruch);
        var team = rollen
            .GroupBy(r => r.PersonId)
            .Select(g => (Rolle: g.Min(r => r.Rolle), Steckbrief: personen.GetValueOrDefault(g.Key)))
            .Where(p => !string.IsNullOrWhiteSpace(p.Steckbrief?.Vorname))
            .OrderBy(p => p.Rolle)
            .ThenBy(p => p.Steckbrief!.Vorname, StringComparer.CurrentCulture)
            .ThenBy(p => p.Steckbrief!.Nachname ?? "", StringComparer.CurrentCulture)
            .Select(p => new TeamPerson(
                p.Steckbrief!.Vorname!.Trim(),
                Initialen(p.Steckbrief.Vorname, p.Steckbrief.Nachname),
                FreizeitRechte.Api(p.Rolle),
                string.IsNullOrWhiteSpace(p.Steckbrief.Herkunft) ? null : p.Steckbrief.Herkunft.Trim()))
            .ToList();
        var ausgebucht = Platzlage.Ausgebucht(freizeit.HoechstzahlTeilnehmende, await bestaetigte.AnzahlAsync(freizeit.Id, abbruch), link.Art);

        return TypedResults.Ok(new ApiFreizeitseite(
            angaben.Name,
            freizeit.Name,
            freizeit.ErsterTag,
            freizeit.LetzterTag,
            freizeit.Ort,
            freizeit.Beschreibung,
            AnmeldelinkErzeugen.Api(link.Art),
            ausgebucht,
            team,
            freizeit.Teilnahmebedingungen,
            await OeffentlichesDokumentEndpunkt.VerweiseAsync(db, freizeit, orgDokumente, abbruch)));
    }

    /// <summary>
    /// Erstes Schriftzeichen (Textelement, auch zusammengesetzte Buchstaben) von Vor- und Nachname, groß; ohne Nachname nur das des
    /// Vornamens.
    /// </summary>
    internal static string Initialen(string? vorname, string? nachname) =>
        string.Concat(new[] { vorname, nachname }
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => StringInfo.GetNextTextElement(t!.Trim().Normalize(System.Text.NormalizationForm.FormC)).ToUpper(CultureInfo.GetCultureInfo("de-DE"))));
}

/// <summary>
/// <c>GET /api/freizeiten/oeffentlich/{token}/vertrauensseite</c> (anonym, zugriffsbegrenzt, FR-66): die gespeicherten Einstellungen
/// von Freizeit (Name, Daten, Freigabe-Intervall, „Freigabe bis“, Fragen des Freizeitpasses je Zielgruppe) und Organisation (Name,
/// Löschfristen, Pflicht-Nachweise).
/// Mandant aus dem Token. Ungültiger Link <c>404</c>.
/// </summary>
internal static class VertrauensseiteEndpunkt
{
    public static async Task<Results<Ok<Vertrauensseite>, NotFound>> Laden(
        string token, FreizeitenLeseDbContext db, MandantenKontext mandant, IVertrauensAngabenLesen organisation, TimeProvider uhr, CancellationToken abbruch)
    {
        if (await AnmeldelinkSuche.FindenAsync(db, mandant, token, uhr.GetUtcNow(), abbruch) is not { } gefunden
            || await organisation.LesenAsync(abbruch) is not { } angaben)
        {
            return TypedResults.NotFound();
        }

        var freizeit = gefunden.Freizeit;

        return TypedResults.Ok(new Vertrauensseite(
            angaben.Name,
            freizeit.Name,
            freizeit.ErsterTag,
            freizeit.LetzterTag,
            freizeit.FreigabeIntervallTage,
            FreizeitZeiten.FreigabeBis(freizeit.LetzterTag, freizeit.FreigabeIntervallTage),
            angaben.FreizeitDatenMonate,
            angaben.NachweisBestaetigungMonate,
            [.. angaben.PflichtArten.Select(Bezeichnung)],
            Fragetexte(freizeit.FragenTeilnehmende),
            Fragetexte(freizeit.FragenMitarbeitende)));
    }

    /// <summary>Die Texte der gewählten Fragen aus dem Katalog von Personen (Story 2-4, FR-22), in Katalogreihenfolge.</summary>
    private static List<string> Fragetexte(IEnumerable<string> kennungen) =>
        [.. Fragenkatalog.Ordnen(kennungen).Select(k => Fragenkatalog.Finden(k)!.Frage)];

    /// <summary>Deutsche Bezeichnung einer Nachweisart (Glossar).</summary>
    internal static string Bezeichnung(OrgNachweisArt art) => art switch
    {
        OrgNachweisArt.Fuehrungszeugnis => "Führungszeugnis",
        OrgNachweisArt.Juleica => "JuLeiCa",
        OrgNachweisArt.ErsteHilfe => "Erste Hilfe",
        OrgNachweisArt.Praeventionsschulung => "Präventionsschulung",
        _ => art.ToString(),
    };
}
