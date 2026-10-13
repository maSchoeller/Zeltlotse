using System.Globalization;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Api.Contracts.Programm;
using Zeltlotse.Ui.Texte;

namespace Zeltlotse.App.Features.Programm;

/// <summary>
/// Story 3-1: Gemeinsames der Programm-Seiten: Namen der übernehmenden Personen (der Client setzt sie zusammen, AD-2: die Leitung aus der Freizeit,
/// das Team aus der Personenliste), das Status-Wort und die Ziele der Links.
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
}
