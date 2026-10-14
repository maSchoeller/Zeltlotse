using System.Globalization;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Api.Contracts.Programm;
using Zeltlotse.Ui.Komponenten;
using Zeltlotse.Ui.Texte;

namespace Zeltlotse.App.Features.Programm;

/// <summary>
/// Story 3-1: Gemeinsames der Programm-Seiten: Namen der übernehmenden Personen (der Client setzt sie zusammen, AD-2: die Leitung aus der Freizeit,
/// das Team aus der Personenliste), das Status-Wort und die Ziele der Links. Story 3-2: „Bringt mit: …“, die vier Material-Zustände und das Ziel
/// der Materialliste.
/// </summary>
internal static class ProgrammTexte
{
    public const string AbgeschlossenText = "Diese Freizeit ist abgeschlossen und nur lesbar.";

    public const string KonfliktText = "Jemand anderes hat diesen Programmpunkt inzwischen übernommen.";

    /// <summary>Je Person der Leitung und des Teams der Name; fehlt eine Quelle, fehlen nur ihre Namen.</summary>
    public static IReadOnlyDictionary<Guid, string> Namen(FreizeitAntwort? freizeit, IEnumerable<PersonenlisteZeile>? personen)
    {
        var namen = new Dictionary<Guid, string>();
        foreach (var person in (personen ?? []).Where(p => p.Art == AnmeldungArt.Mitarbeitende))
        {
            namen[person.PersonId] = Anzeige.Name(person.Vorname, person.Nachname);
        }

        foreach (var leitung in freizeit?.Leitung ?? [])
        {
            namen[leitung.PersonId] = Anzeige.Name(leitung.Vorname, leitung.Nachname);
        }

        return namen;
    }

    /// <summary>„Übernommen: Lea Feuerstein“, ohne bekannten Namen „Übernommen“; nicht übernommen <c>null</c>.</summary>
    public static string? StatusWort(Programmpunkt punkt, IReadOnlyDictionary<Guid, string> namen) =>
        punkt.UebernommenVon is { } person ? Anzeige.Uebernommen(namen.GetValueOrDefault(person)) : null;

    /// <summary>Der Wochenraster mit gewähltem Tag.</summary>
    public static string RasterZiel(Guid freizeitId, DateOnly tag) =>
        $"freizeiten/{freizeitId}/programm?tag={tag.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

    public static string DetailZiel(Guid freizeitId, Guid punktId) => $"freizeiten/{freizeitId}/programm/{punktId}";

    /// <summary>Story 3-2: die Materialliste der Freizeit.</summary>
    public static string MaterialZiel(Guid freizeitId) => $"freizeiten/{freizeitId}/programm/material";

    public const string MaterialKonfliktText = "Jemand anderes bringt dieses Material inzwischen mit.";

    /// <summary>Story 3-2: „Bringt mit: Miriam Zeltner“, ohne bekannten Namen „Bringt jemand mit“; bringt es niemand mit <c>null</c>.</summary>
    public static string? BringtMitWort(Material material, IReadOnlyDictionary<Guid, string> namen) =>
        material.BringtMit is { } person ? Anzeige.BringtMit(namen.GetValueOrDefault(person)) : null;

    /// <summary>Story 3-2: die vier Zustände als Segmente „gebraucht / vorhanden / besorgt / eingepackt“ (FR-36).</summary>
    public static IReadOnlyList<AuswahlOption> MaterialStatusOptionen { get; } =
        [.. Enum.GetValues<MaterialStatus>().Select(s => new AuswahlOption(s.ToString(), Anzeige.MaterialStatus(s)))];

    /// <summary>Story 3-5: die Aufgaben der Freizeit, mit <paramref name="meine"/> der Reiter „Meine Aufgaben“.</summary>
    public static string AufgabenZiel(Guid freizeitId, bool meine = false) =>
        meine ? $"freizeiten/{freizeitId}/aufgaben?meine=true" : $"freizeiten/{freizeitId}/aufgaben";

    /// <summary>Story 3-5: die Detailseite einer Aufgabe.</summary>
    public static string AufgabeZiel(Guid freizeitId, Guid aufgabeId) => $"freizeiten/{freizeitId}/aufgaben/{aufgabeId}";

    public const string AufgabeKonfliktText = "Jemand anderes hat diese Aufgabe inzwischen übernommen.";

    /// <summary>Story 3-5: „Übernommen: Lea Feuerstein“, ohne bekannten Namen „Übernommen“; nicht übernommen „Offen“.</summary>
    public static string AufgabeStatusWort(Aufgabe aufgabe, IReadOnlyDictionary<Guid, string> namen) =>
        aufgabe.UebernommenVon is { } person ? Anzeige.Uebernommen(namen.GetValueOrDefault(person)) : "Offen";

    /// <summary>Story 3-5: die drei Phasen als Auswahl „Vorbereitung / Während der Freizeit / Nachbereitung“.</summary>
    public static IReadOnlyList<AuswahlOption> AufgabenPhaseOptionen { get; } =
        [.. Enum.GetValues<AufgabenPhase>().Select(p => new AuswahlOption(p.ToString(), Anzeige.AufgabenPhase(p)))];

    /// <summary>
    /// Story 3-5: Die Phase der Aufgaben, die zur Phase der Freizeit passt (Vorbelegung beim Anlegen und Grenze des Warnhinweises): in Vorbereitung
    /// Vorbereitung, während der Freizeit Während, danach Nachbereitung.
    /// </summary>
    public static AufgabenPhase AufgabenPhaseZu(Zeltlotse.Api.Contracts.Gemeinsam.Phase phase) => phase switch
    {
        Zeltlotse.Api.Contracts.Gemeinsam.Phase.InVorbereitung => AufgabenPhase.Vorbereitung,
        Zeltlotse.Api.Contracts.Gemeinsam.Phase.Laeuft => AufgabenPhase.Waehrend,
        _ => AufgabenPhase.Nachbereitung,
    };

    /// <summary>
    /// Story 3-5: Warnhinweis im Cockpit: offene, nicht übernommene Aufgaben, deren Phase höchstens der Phase der Freizeit entspricht (in der
    /// Vorbereitung nur Vorbereitung, während der Freizeit auch Während, danach alle).
    /// </summary>
    public static int AufgabenUnbesetzt(IEnumerable<Aufgabe> aufgaben, Zeltlotse.Api.Contracts.Gemeinsam.Phase phase)
    {
        var grenze = AufgabenPhaseZu(phase);
        return aufgaben.Count(a => !a.Erledigt && a.UebernommenVon is null && a.Phase <= grenze);
    }
}
