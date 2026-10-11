namespace Zeltlotse.Konten.Domaene;

/// <summary>Mail-Adressen werden getrimmt und klein geschrieben gespeichert und verglichen.</summary>
internal static class MailAdresse
{
    public const int MaximaleLaenge = 254;

    public static string Normalisieren(string mail) => mail.Trim().ToLowerInvariant();
}
