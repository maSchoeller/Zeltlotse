using Zeltlotse.Freizeiten.Contracts.Events;
using Zeltlotse.Personen.Contracts.Freizeitpass;

namespace Zeltlotse.Freizeiten.Domaene;

/// <summary>
/// Story 2-5, AD-4: Ereignisse tragen die Fragen des Freizeitpasses als <see cref="FreizeitpassFrage"/>, nie als Text. Der Name des
/// Mitglieds ist die PascalCase-Form der Katalog-Kennung (<see cref="Fragenkatalog.PascalName"/>).
/// </summary>
internal static class FragenImEreignis
{
    /// <summary>Die Kennungen als Enum-Mitglieder in derselben Reihenfolge; eine Kennung ohne Mitglied ist ein Programmfehler.</summary>
    public static FreizeitpassFrage[] Aus(IEnumerable<string> kennungen) =>
    [
        .. kennungen.Select(k => Enum.TryParse<FreizeitpassFrage>(Fragenkatalog.PascalName(k), ignoreCase: false, out var frage) && Enum.IsDefined(frage)
            ? frage
            : throw new InvalidOperationException($"Für die Frage {k} gibt es kein Mitglied in {nameof(FreizeitpassFrage)}.")),
    ];

    /// <summary>Die Katalog-Kennung eines Mitglieds.</summary>
    public static string Kennung(FreizeitpassFrage frage) =>
        Fragenkatalog.KennungAusPascalName(frage.ToString())
        ?? throw new InvalidOperationException($"{nameof(FreizeitpassFrage)}.{frage} hat keine Frage im Katalog.");
}
