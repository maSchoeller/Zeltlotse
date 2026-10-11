namespace Zeltlotse.Konten.Anmelden;

/// <summary>Text der Login-Mail: Deutsch, ohne Anrede (EXPERIENCE.md → Voice and Tone), mit Link und Code.</summary>
internal static class LoginMail
{
    public const string Betreff = "Anmelden bei Zeltlotse";

    /// <summary>Seite der Web-App, die den Link zeigt und erst beim Klick einlöst (AD-11).</summary>
    public static string Link(string oeffentlicheAdresse, string token) => $"{oeffentlicheAdresse}/app/anmelden/link/{token}";

    public static string Text(string oeffentlicheAdresse, string token, string code) =>
        $"""
        Mit diesem Link geht es zur Anmeldung bei Zeltlotse:

        {Link(oeffentlicheAdresse, token)}

        In der installierten App reicht dieser Login-Code: {code}

        Link und Code gelten 15 Minuten und nur einmal. Wer diese Anmeldung nicht angefordert hat, kann die Mail einfach löschen.
        """;
}
