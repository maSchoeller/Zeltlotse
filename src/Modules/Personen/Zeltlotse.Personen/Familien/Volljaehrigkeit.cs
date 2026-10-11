namespace Zeltlotse.Personen.Familien;

/// <summary>
/// Die einzige Stelle, die Volljährigkeit berechnet (FR-4): volljährig ab dem 18. Geburtstag, als Kalendertag in
/// Europe/Berlin nach der Uhr (<see cref="TimeProvider"/>). Am 29. Februar Geborene sind in Nicht-Schaltjahren ab dem
/// 28. Februar volljährig (<see cref="DateOnly.AddYears"/>). Der Server liefert das Ergebnis als <c>volljaehrig</c>; der
/// Client rechnet nie selbst.
/// </summary>
internal static class Volljaehrigkeit
{
    public const int Alter = 18;

    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    /// <summary>Der heutige Kalendertag in Europe/Berlin.</summary>
    public static DateOnly Heute(TimeProvider uhr) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(uhr.GetUtcNow(), Berlin).DateTime);

    public static bool Ist(DateOnly geburtsdatum, DateOnly heute) => geburtsdatum.AddYears(Alter) <= heute;
}
