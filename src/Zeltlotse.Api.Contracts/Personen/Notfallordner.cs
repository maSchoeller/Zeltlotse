namespace Zeltlotse.Api.Contracts.Personen;

/// <summary>
/// Ein Eintrag von <c>GET /api/freizeiten/{freizeitId}/personen/notfallordner?person=…</c> (Leitung und Team, Story 2-11, FR-33, AD-8):
/// eine bestätigte Person mit gültiger Freigabe und genau den Fragen dieser Freigabe in Katalogreihenfolge (unbeantwortet mit
/// <c>wert: null</c>). Die Liste ist nach Nachname, dann Vorname sortiert; Personen ohne bestätigte, gültige Freigabe fehlen still.
/// </summary>
public sealed record NotfallordnerPerson(Guid PersonId, string Vorname, string Nachname, IReadOnlyList<FreizeitpassFrage> Fragen);

/// <summary>Abfrageparameter von <c>…/personen/notfallordner</c>: Name und Höchstzahl der Kennungen je Abruf.</summary>
public static class NotfallordnerAbfrage
{
    /// <summary>Name des Abfrageparameters, der sich je Person wiederholt.</summary>
    public const string Parameter = "person";

    /// <summary>
    /// Höchstzahl der Kennungen je Abruf: 150 Kennungen ergeben eine Anfragezeile unter Kestrels Grenze von 8192 Byte.
    /// </summary>
    public const int HoechstensKennungen = 150;
}
