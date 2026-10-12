using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;

namespace Zeltlotse.Freizeiten.Domaene;

/// <summary>
/// Phasen und Zeitpunkte einer Freizeit (AD-17, FR-45, FR-24d): Tagesgrenzen gelten in Europe/Berlin. Die Freizeit beginnt
/// um 00:00 des ersten Tages und endet um 00:00 des Tages nach dem letzten Tag (Freizeitende); das Freigabefenster gilt bis
/// Freizeitende plus Intervall in Kalendertagen. Nur der Abschluss ist gespeichert; alles andere folgt aus Datum und Uhr.
/// </summary>
internal static class FreizeitZeiten
{
    /// <summary>
    /// Vorgabe für das Freigabe-Intervall in Tagen (NFR-5), bis es eine Org-Richtlinie gibt; beim Anlegen an der Freizeit
    /// festgeschrieben.
    /// </summary>
    public const int FreigabeIntervallVorgabeTage = 30;

    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    /// <summary>00:00 Uhr des Tages in Europe/Berlin als Zeitpunkt (Mitternacht fällt in Berlin nie in eine Zeitumstellung).</summary>
    public static DateTimeOffset TagesBeginn(DateOnly tag) =>
        new(TimeZoneInfo.ConvertTimeToUtc(tag.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), Berlin), TimeSpan.Zero);

    /// <summary>Der Kalendertag eines Zeitpunkts in Europe/Berlin („heute“ nach der Uhr).</summary>
    public static DateOnly Heute(DateTimeOffset jetzt) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(jetzt, Berlin).DateTime);

    /// <summary>Ende des letzten Freizeittages: 24:00 Uhr Europe/Berlin.</summary>
    public static DateTimeOffset Freizeitende(DateOnly letzterTag) => TagesBeginn(letzterTag.AddDays(1));

    /// <summary>Ende des Freigabefensters: Freizeitende plus <paramref name="intervallTage"/> Kalendertage in Berlin.</summary>
    public static DateTimeOffset FreigabeGueltigBis(DateOnly letzterTag, int intervallTage) => TagesBeginn(letzterTag.AddDays(1 + intervallTage));

    /// <summary>Der letzte Tag des Freigabefensters, wie er angezeigt wird: letzter Freizeittag plus Intervall.</summary>
    public static DateOnly FreigabeBis(DateOnly letzterTag, int intervallTage) => letzterTag.AddDays(intervallTage);

    public static FreizeitPhase Phase(DateOnly ersterTag, DateOnly letzterTag, DateTimeOffset? abgeschlossenAm, DateTimeOffset jetzt)
    {
        if (abgeschlossenAm is not null)
        {
            return FreizeitPhase.Gelaufen;
        }

        if (jetzt < TagesBeginn(ersterTag))
        {
            return FreizeitPhase.InVorbereitung;
        }

        return jetzt < Freizeitende(letzterTag) ? FreizeitPhase.Laeuft : FreizeitPhase.Nachbereitung;
    }
}
