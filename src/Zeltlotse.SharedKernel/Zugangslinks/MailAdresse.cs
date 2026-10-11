namespace Zeltlotse.SharedKernel.Zugangslinks;

/// <summary>
/// Mail-Adressen werden getrimmt und klein geschrieben gespeichert und verglichen (Konten, Einladungen). Erlaubt ist nur
/// eine nackte Adresse, damit eine Mail genau an die gespeicherte Adresse geht.
/// </summary>
public static class MailAdresse
{
    public const int MaximaleLaenge = 254;

    public static string Normalisieren(string mail) => mail.Trim().ToLowerInvariant();

    /// <summary>
    /// Nur eine nackte Adresse: kein Anzeigename (<c>x&lt;andere@y.de&gt;</c>), keine Liste, keine Anführungszeichen, keine
    /// Leerzeichen. So geht die Mail genau an die gespeicherte Adresse.
    /// </summary>
    public static bool IstReineMailAdresse(string mail) =>
        !mail.Any(z => char.IsWhiteSpace(z) || z is '"' or ',' or ';' or '<' or '>')
        && System.Net.Mail.MailAddress.TryCreate(mail, out var adresse)
        && string.IsNullOrEmpty(adresse.DisplayName)
        && string.Equals(adresse.Address, mail, StringComparison.Ordinal);

    /// <summary>Getrimmt, nicht zu lang und eine nackte Adresse.</summary>
    public static bool IstGueltig(string? mail) =>
        !string.IsNullOrWhiteSpace(mail) && mail.Trim().Length <= MaximaleLaenge && IstReineMailAdresse(mail.Trim());
}
