using Microsoft.Extensions.Configuration;
using Zeltlotse.SharedKernel.Mail;
using Zeltlotse.SharedKernel.Web;

namespace Zeltlotse.Personen.Familien;

/// <summary>
/// Texte der Familien-Einladung und der Profil-Übergabe: Deutsch, ohne Anrede (EXPERIENCE.md → Voice and Tone), mit Link und
/// Gültigkeit. Die Mails gehen direkt nach dem Command aus dem Endpunkt (AD-13, Ausnahme „Einladungen“). Mail-Adresse und
/// Token erscheinen nie in Logs (AD-22).
/// </summary>
internal static class FamilienMails
{
    public const string EinladungsBetreff = "Einladung ins Familienkonto";

    public const string UebergabeBetreff = "Profil in Zeltlotse übernehmen";

    /// <summary>Seite der Web-App, die die Einladung zeigt und erst beim Klick annimmt (AD-11).</summary>
    public static string EinladungsLink(string oeffentlicheAdresse, string token) => $"{oeffentlicheAdresse}/app/familie/einladung/{token}";

    /// <summary>Seite der Web-App, die die Übergabe zeigt und erst beim Klick annimmt (AD-11).</summary>
    public static string UebergabeLink(string oeffentlicheAdresse, string token) => $"{oeffentlicheAdresse}/app/uebergabe/{token}";

    public static string EinladungsText(string oeffentlicheAdresse, string token) =>
        $"""
        Einladung in Zeltlotse als zweiter Elternteil eines Familienkontos. Beide Eltern haben im Familienkonto dieselben Rechte.

        Mit diesem Link geht es zur Einladung:

        {EinladungsLink(oeffentlicheAdresse, token)}

        Der Link gilt 7 Tage und nur einmal. Zum Annehmen mit dieser Mail-Adresse anmelden.
        """;

    public static string UebergabeText(string oeffentlicheAdresse, string? vorname, string token) =>
        $"""
        Das Profil von {(string.IsNullOrWhiteSpace(vorname) ? "einem Kind" : vorname)} in Zeltlotse ist bereit zur Übernahme. Nach der Übernahme gehört es zum eigenen Konto, und das Familienkonto hat keinen Zugriff mehr.

        Mit diesem Link geht es zur Übernahme:

        {UebergabeLink(oeffentlicheAdresse, token)}

        Der Link gilt 14 Tage und nur einmal. Zum Annehmen mit dieser Mail-Adresse anmelden.
        """;

    public static Task EinladungVerschickenAsync(IMailVersand mail, IConfiguration konfiguration, ElternteilEinladung.Neu neu, CancellationToken abbruch) =>
        mail.SendenAsync(neu.Mail, EinladungsBetreff, EinladungsText(OeffentlicheAdresse.Lesen(konfiguration), neu.Token), abbruch);

    public static Task UebergabeVerschickenAsync(IMailVersand mail, IConfiguration konfiguration, UebergabeAusloesung.Neu neu, CancellationToken abbruch) =>
        mail.SendenAsync(neu.Mail, UebergabeBetreff, UebergabeText(OeffentlicheAdresse.Lesen(konfiguration), neu.Vorname, neu.Token), abbruch);
}
