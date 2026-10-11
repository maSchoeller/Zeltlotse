using Microsoft.Extensions.Configuration;
using Zeltlotse.SharedKernel.Mail;
using Zeltlotse.SharedKernel.Web;

namespace Zeltlotse.Freizeiten.Leitung;

/// <summary>
/// Text der Leitungs-Einladung: Deutsch, ohne Anrede (EXPERIENCE.md → Voice and Tone), mit Name der Freizeit, Rolle, Link
/// und Gültigkeit. Die Mail geht direkt nach dem Command aus dem Endpunkt (AD-13, Ausnahme „Einladungen“).
/// </summary>
internal static class LeitungsMail
{
    public const string Betreff = "Einladung als Leitung in Zeltlotse";

    /// <summary>Seite der Web-App, die die Einladung zeigt und erst beim Klick annimmt (AD-11).</summary>
    public static string Link(string oeffentlicheAdresse, string token) => $"{oeffentlicheAdresse}/app/leitung/{token}";

    public static string Text(string oeffentlicheAdresse, string freizeit, string token) =>
        $"""
        Einladung in Zeltlotse für die Freizeit „{freizeit}“ mit der Rolle „Leitung“.

        Mit diesem Link geht es zur Einladung:

        {Link(oeffentlicheAdresse, token)}

        Der Link gilt 7 Tage und nur einmal. Zum Annehmen mit dieser Mail-Adresse anmelden.
        """;

    /// <summary>Schickt die Mail; Mail-Adresse und Token erscheinen nie in Logs (AD-22).</summary>
    public static Task VerschickenAsync(IMailVersand mail, IConfiguration konfiguration, NeueLeitungsEinladung neu, CancellationToken abbruch) =>
        mail.SendenAsync(neu.Mail, Betreff, Text(OeffentlicheAdresse.Lesen(konfiguration), neu.FreizeitName, neu.Token), abbruch);
}
