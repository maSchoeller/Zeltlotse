using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Personen.Familien;
using NachweisArt = Zeltlotse.Personen.Contracts.Events.NachweisArt;

namespace Zeltlotse.Personen.Nachweise;

/// <summary>
/// Feldregeln der Nachweise, gemeinsam für Anlegen, Bearbeiten und Ablehnen. Jede Regel liefert den Fehlertext für das Feld
/// oder <c>null</c>. Tagesgrenzen gelten in Europe/Berlin.
/// </summary>
internal static class NachweisRegeln
{
    /// <summary>Frühestes erlaubtes Ausstellungsjahr; davor gilt ein Datum als Tippfehler.</summary>
    public const int FruehestesJahr = 1900;

    public static string? Art(NachweisArt? art) => art is { } wert && Enum.IsDefined(wert) ? null : "Art fehlt.";

    public static string? Ausstellungsdatum(DateOnly? datum, DateOnly heute) => datum switch
    {
        null => "Ausstellungsdatum fehlt.",
        { Year: < FruehestesJahr } => "Das Ausstellungsdatum ist ungültig.",
        { } tag when tag > heute => "Das Ausstellungsdatum liegt in der Zukunft.",
        _ => null,
    };

    /// <summary>Das Ausstellungsdatum als Text im <see cref="NachweisFelder.Datumsformat"/> (PATCH, AD-19).</summary>
    public static string? Ausstellungsdatum(string? wert, DateOnly heute)
    {
        if (string.IsNullOrWhiteSpace(wert))
        {
            return "Ausstellungsdatum fehlt.";
        }

        return DateOnly.TryParseExact(wert.Trim(), NachweisFelder.Datumsformat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var tag)
            ? Ausstellungsdatum(tag, heute)
            : "Das Ausstellungsdatum ist ungültig.";
    }

    public static string? Hinweis(string? hinweis) =>
        string.IsNullOrWhiteSpace(hinweis) ? "Hinweis fehlt."
        : hinweis.Trim().Length > NachweisFelder.HinweisMaximaleLaenge ? $"Der Hinweis ist zu lang (höchstens {NachweisFelder.HinweisMaximaleLaenge} Zeichen)."
        : null;

    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    /// <summary>Der heutige Kalendertag in Europe/Berlin.</summary>
    public static DateOnly Heute(TimeProvider uhr) => Volljaehrigkeit.Heute(uhr);

    /// <summary>Der Kalendertag eines Zeitpunkts in Europe/Berlin.</summary>
    public static DateOnly Tag(DateTimeOffset zeitpunkt) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(zeitpunkt, Berlin).DateTime);

    /// <summary>Endung einer Datei je erkanntem Typ, für den Dateinamen <c>&lt;art&gt;.&lt;endung&gt;</c>.</summary>
    public static string Endung(SharedKernel.Dateien.DateiArt art) => art switch
    {
        SharedKernel.Dateien.DateiArt.Jpeg => "jpg",
        SharedKernel.Dateien.DateiArt.Png => "png",
        SharedKernel.Dateien.DateiArt.WebP => "webp",
        SharedKernel.Dateien.DateiArt.Pdf => "pdf",
        _ => "bin",
    };

    /// <summary>Gleichzeitige Erinnerungen derselben Freizeit an dieselbe Person laufen nacheinander (höchstens eine je Tag).</summary>
    public static Task ErinnerungSperrenAsync(DbContext db, Guid freizeitId, Guid personId, CancellationToken abbruch) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"erinnerung:" + freizeitId + ":" + personId}, 0))", abbruch);

    /// <summary>Gleichzeitige Anfragen derselben Person in derselben Organisation laufen nacheinander (höchstens eine offene Einsicht).</summary>
    public static Task EinsichtSperrenAsync(DbContext db, Guid personId, Guid organisationId, CancellationToken abbruch) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"einsicht:" + personId + ":" + organisationId}, 0))", abbruch);
}
