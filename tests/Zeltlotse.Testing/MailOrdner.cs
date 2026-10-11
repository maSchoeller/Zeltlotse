using System.Text.RegularExpressions;
using MimeKit;

namespace Zeltlotse.Testing;

/// <summary>Eine gelesene Mail aus dem Ordner-Adapter.</summary>
public sealed record GeleseneMail(string An, string Betreff, string Text)
{
    /// <summary>
    /// Der Link aus der Mail: Login-Link (<c>…/app/anmelden/link/&lt;token&gt;</c>), Einladungslink
    /// (<c>…/app/einladung/&lt;token&gt;</c>) oder Leitungs-Einladung (<c>…/app/leitung/&lt;token&gt;</c>).
    /// </summary>
    public string Link => LoginMailMuster.Link().Match(Text) is { Success: true } treffer
        ? treffer.Value
        : throw new InvalidOperationException("Die Mail enthält keinen Login- oder Einladungslink.");

    /// <summary>Das Token aus dem Link.</summary>
    public string Token => Link[(Link.LastIndexOf('/') + 1)..];

    /// <summary>Der sechsstellige Login-Code aus der Mail.</summary>
    public string Code => LoginMailMuster.Code().Match(Text) is { Success: true } treffer
        ? treffer.Groups[1].Value
        : throw new InvalidOperationException("Die Mail enthält keinen Login-Code.");
}

/// <summary>Liest die <c>.eml</c>-Dateien des Mail-Adapters <c>Ordner</c> (Tests und Entwicklung).</summary>
public sealed class MailOrdner(string ordner)
{
    /// <summary>Alle Mails, älteste zuerst.</summary>
    public IReadOnlyList<GeleseneMail> Alle()
    {
        if (!Directory.Exists(ordner))
        {
            return [];
        }

        return
        [
            .. new DirectoryInfo(ordner).EnumerateFiles("*.eml")
                .OrderBy(d => d.LastWriteTimeUtc)
                .ThenBy(d => d.Name, StringComparer.Ordinal)
                .Select(d => Lesen(d.FullName)),
        ];
    }

    /// <summary>Alle Mails an eine Adresse (ohne Groß- und Kleinschreibung), älteste zuerst.</summary>
    public IReadOnlyList<GeleseneMail> An(string adresse) =>
        [.. Alle().Where(m => string.Equals(m.An, adresse.Trim(), StringComparison.OrdinalIgnoreCase))];

    /// <summary>Die neueste Mail an eine Adresse.</summary>
    public GeleseneMail LetzteAn(string adresse) =>
        An(adresse).LastOrDefault() ?? throw new InvalidOperationException("Keine Mail an diese Adresse.");

    /// <summary>Wartet bis zu <paramref name="wartezeit"/>, bis mindestens <paramref name="anzahl"/> Mails an die Adresse da sind.</summary>
    public async Task<GeleseneMail> WartenAufAsync(string adresse, int anzahl = 1, TimeSpan? wartezeit = null)
    {
        var bis = DateTime.UtcNow + (wartezeit ?? TimeSpan.FromSeconds(30));
        while (true)
        {
            var mails = An(adresse);
            if (mails.Count >= anzahl)
            {
                return mails[^1];
            }

            if (DateTime.UtcNow > bis)
            {
                throw new TimeoutException($"Nach {wartezeit ?? TimeSpan.FromSeconds(30)} keine {anzahl}. Mail an die Adresse.");
            }

            await Task.Delay(100);
        }
    }

    private static GeleseneMail Lesen(string datei)
    {
        using var nachricht = MimeMessage.Load(datei);
        var an = nachricht.To.Mailboxes.Single().Address;
        return new GeleseneMail(an, nachricht.Subject ?? "", nachricht.TextBody ?? "");
    }
}

internal static partial class LoginMailMuster
{
    [GeneratedRegex(@"https?://\S+/app/(?:anmelden/link|einladung|leitung)/[A-Za-z0-9_-]+")]
    public static partial Regex Link();

    [GeneratedRegex(@"Login-Code: (\d{6})")]
    public static partial Regex Code();
}
