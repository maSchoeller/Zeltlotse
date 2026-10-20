using Microsoft.AspNetCore.Http;
using Zeltlotse.Api.Contracts.Finanzen;
using Zeltlotse.Finanzen.Kalkulation;
using Zeltlotse.Freizeiten.Contracts.Team;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Dateien;
using ApiBeleg = Zeltlotse.Api.Contracts.Finanzen.Beleg;

namespace Zeltlotse.Finanzen.Belege;

/// <summary>Story 5-2: Ergebnis der Commands zu Belegen.</summary>
internal abstract record BelegAenderung
{
    private BelegAenderung()
    {
    }

    /// <summary>Angelegt oder geändert: die verfolgte Zeile (nach dem Speichern mit neuer Version).</summary>
    public sealed record Gespeichert(Beleg Beleg) : BelegAenderung;

    public sealed record Geloescht : BelegAenderung;

    /// <summary>Derselbe Wert wurde inzwischen geändert: <c>409 konflikt</c> (AD-19).</summary>
    public sealed record Konflikt : BelegAenderung;

    /// <summary>Die Freizeit ist abgeschlossen und nur lesbar: <c>409 freizeit-abgeschlossen</c> (AD-17).</summary>
    public sealed record Abgeschlossen : BelegAenderung;

    /// <summary>Freizeit oder Beleg gibt es hier nicht: <c>404</c>.</summary>
    public sealed record NichtGefunden : BelegAenderung;

    /// <summary>„Bezahlt von“ gehört nicht zu Leitung oder Team der Freizeit: <c>400</c> am Feld <c>bezahltVon</c>.</summary>
    public sealed record FremderZahler : BelegAenderung;
}

/// <summary>Story 5-2: Gemeinsame Schritte der Beleg-Handler und -Endpunkte.</summary>
internal static class BelegSchritte
{
    public const string BetragFehlt = "Betrag fehlt.";

    public const string KategorieFehlt = "Kategorie fehlt.";

    public const string KategorieUnbekannt = "Unbekannte Kategorie.";

    public const string ZahlerFehler = "Nur eine Person aus Leitung oder Team der Freizeit.";

    /// <summary>
    /// Die zweite Prüfung des Schreibschutzes im Handler (AD-17; die Pipeline prüft zuerst): <c>null</c>, wenn es weitergehen kann; sonst nicht gefunden
    /// oder abgeschlossen.
    /// </summary>
    public static async Task<BelegAenderung?> FreizeitPruefenAsync(IFreizeitZeitpunkte zeitpunkte, Guid freizeitId, CancellationToken abbruch)
    {
        if (!(await zeitpunkte.ZeitpunkteAsync([freizeitId], abbruch)).TryGetValue(freizeitId, out var freizeit))
        {
            return new BelegAenderung.NichtGefunden();
        }

        return freizeit.Phase == FreizeitPhase.Gelaufen ? new BelegAenderung.Abgeschlossen() : null;
    }

    /// <summary>Ob die Person zu Leitung oder Team der Freizeit gehört (über Freizeiten, Pfeil Finanzen → Freizeiten).</summary>
    public static async Task<bool> ImTeamAsync(IFreizeitTeamLesen team, Guid freizeitId, Guid personId, CancellationToken abbruch) =>
        (await team.TeamAsync(freizeitId, abbruch)).Any(m => m.PersonId == personId);

    /// <summary>Der Fehler zu einer Kategorie oder <c>null</c>.</summary>
    public static string? KategorieFehler(string? text) =>
        FinanzenWerte.TextOderNull(text) is null ? KategorieFehlt : Kostenkategorien.Lesen(text) is null ? KategorieUnbekannt : null;

    /// <summary>Der Fehler zu einem Pflichtbetrag oder <c>null</c>.</summary>
    public static string? BetragFehler(string? text) => FinanzenWerte.TextOderNull(text) is null ? BetragFehlt : FinanzenWerte.BetragFehler(text);

    /// <summary>Der Fehler zu einer optionalen Bezeichnung oder <c>null</c>.</summary>
    public static string? BezeichnungFehler(string? text) =>
        FinanzenWerte.TextOderNull(text) is { Length: > BelegFelder.BezeichnungMaximaleLaenge } ? $"Höchstens {BelegFelder.BezeichnungMaximaleLaenge} Zeichen." : null;

    /// <summary>Ein Beleg, wie die API ihn liefert (Betrag ohne Null-Nachkommastellen).</summary>
    public static ApiBeleg AlsApi(Beleg beleg) => new(
        beleg.Id,
        beleg.Version,
        beleg.Kategorie,
        FinanzenWerte.OhneNullen(beleg.Betrag),
        beleg.Bezeichnung,
        beleg.PersonId,
        beleg.Erstattet,
        beleg.ErfasstAm,
        beleg.DateiArt is { } art ? new BelegDatei(beleg.Dateiname ?? DokumentDateien.Dateiname("Beleg", art), DateiPruefung.IstBild(art)) : null);

    /// <summary>Die gemeinsamen Antworten auf ein Ergebnis; <paramref name="gespeichert"/> baut die Antwort bei Erfolg.</summary>
    public static IResult Antwort(BelegAenderung ergebnis, Func<Beleg, IResult> gespeichert) => ergebnis switch
    {
        BelegAenderung.Gespeichert g => gespeichert(g.Beleg),
        BelegAenderung.Geloescht => TypedResults.NoContent(),
        BelegAenderung.Konflikt => FinanzenProbleme.Konflikt(),
        BelegAenderung.Abgeschlossen => FinanzenProbleme.Abgeschlossen(),
        BelegAenderung.FremderZahler => throw new ValidierungsAusnahme(new Dictionary<string, string[]> { [BelegFelder.BezahltVon] = [ZahlerFehler] }),
        _ => TypedResults.NotFound(),
    };
}
