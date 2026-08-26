using System.Globalization;
using System.Text;

namespace Zeltlotse.Core.Organisationen;

/// <summary>
/// Erzeugt die Adresse einer Organisation aus ihrem Namen. Sie wird einmal
/// vergeben und danach nie mehr geändert — gespeicherte Links bleiben gültig.
/// </summary>
internal static class Slug
{
    private const int Hoechstlaenge = 60;
    private const string Ersatzwert = "organisation";

    public static string AusName(string name)
    {
        var umschrift = Umschreiben(name);
        var gebaut = new StringBuilder(umschrift.Length);

        foreach (var zeichen in umschrift)
        {
            if (char.IsAsciiLetterOrDigit(zeichen))
            {
                gebaut.Append(zeichen);
            }
            else if (gebaut.Length > 0 && gebaut[^1] != '-')
            {
                gebaut.Append('-');
            }
        }

        var slug = gebaut.ToString().Trim('-');

        if (slug.Length > Hoechstlaenge)
        {
            slug = slug[..Hoechstlaenge].TrimEnd('-');
        }

        return slug.Length == 0 ? Ersatzwert : slug;
    }

    /// <summary>
    /// Hängt die kleinste freie Zahl an, wenn die Adresse bereits vergeben ist.
    /// </summary>
    public static string Eindeutig(string name, Func<string, bool> istBelegt)
    {
        var basis = AusName(name);

        if (!istBelegt(basis))
        {
            return basis;
        }

        for (var zaehler = 2; ; zaehler++)
        {
            var anhang = $"-{zaehler}";
            var gekuerzt = basis.Length + anhang.Length > Hoechstlaenge
                ? basis[..(Hoechstlaenge - anhang.Length)].TrimEnd('-')
                : basis;

            var kandidat = gekuerzt + anhang;

            if (!istBelegt(kandidat))
            {
                return kandidat;
            }
        }
    }

    private static string Umschreiben(string name)
    {
        var vorbereitet = name
            .Replace("ä", "ae", StringComparison.Ordinal)
            .Replace("ö", "oe", StringComparison.Ordinal)
            .Replace("ü", "ue", StringComparison.Ordinal)
            .Replace("Ä", "ae", StringComparison.Ordinal)
            .Replace("Ö", "oe", StringComparison.Ordinal)
            .Replace("Ü", "ue", StringComparison.Ordinal)
            .Replace("ß", "ss", StringComparison.Ordinal)
            .ToLowerInvariant();

        // Übrige Diakritika (é, ç, …) auf ihren Grundbuchstaben zurückführen.
        var zerlegt = vorbereitet.Normalize(NormalizationForm.FormD);
        var ohneAkzente = new StringBuilder(zerlegt.Length);

        foreach (var zeichen in zerlegt)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(zeichen) != UnicodeCategory.NonSpacingMark)
            {
                ohneAkzente.Append(zeichen);
            }
        }

        return ohneAkzente.ToString().Normalize(NormalizationForm.FormC);
    }
}
