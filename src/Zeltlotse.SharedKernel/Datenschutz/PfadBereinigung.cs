using System.Text.RegularExpressions;

namespace Zeltlotse.SharedKernel.Datenschutz;

/// <summary>
/// Entfernt aus einer Seitenangabe alles, was ein Geheimnis tragen kann (AD-22): Abfrage und Fragment fallen weg,
/// jedes Pfadsegment aus <c>[A-Za-z0-9_-]</c> mit mindestens 22 Zeichen, das keine <see cref="Guid"/> im Format mit Bindestrichen ist (32 Hex-Zeichen könnten ein Token sein),
/// wird zu <c>***</c> (Token haben mindestens 128 Bit, AD-11).
/// </summary>
public static partial class PfadBereinigung
{
    public const string Ersatz = "***";

    public static string Bereinigen(string? seite)
    {
        if (string.IsNullOrEmpty(seite))
        {
            return "";
        }

        var pfad = seite;
        if (Uri.TryCreate(seite, UriKind.Absolute, out var absolut) && absolut.Scheme is "http" or "https")
        {
            pfad = absolut.AbsolutePath;
        }

        var ende = pfad.IndexOfAny(['?', '#']);
        if (ende >= 0)
        {
            pfad = pfad[..ende];
        }

        return string.Join('/', pfad.Split('/').Select(SegmentBereinigen));
    }

    private static string SegmentBereinigen(string segment) =>
        TokenMuster().IsMatch(segment) && !Guid.TryParseExact(segment, "D", out _) ? Ersatz : segment;

    [GeneratedRegex("^[A-Za-z0-9_-]{22,}$", RegexOptions.CultureInvariant)]
    private static partial Regex TokenMuster();
}
