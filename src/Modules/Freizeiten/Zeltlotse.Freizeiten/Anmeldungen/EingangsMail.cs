using System.Globalization;
using Microsoft.Extensions.Configuration;
using Zeltlotse.Freizeiten.Domaene;
using Zeltlotse.SharedKernel.Mail;
using Zeltlotse.SharedKernel.Web;

namespace Zeltlotse.Freizeiten.Anmeldungen;

/// <summary>
/// Eingangsmail einer Gast-Anmeldung (FR-17, AD-13): selbst angefordert, darum direkt aus dem Endpunkt und nicht über die Outbox,
/// denn das Gast-Token darf nicht in die Outbox. Deutsch, ohne Anrede (EXPERIENCE.md → Voice and Tone), mit Gast-Link und „gilt
/// bis“. Verspricht nichts, was es noch nicht gibt.
/// </summary>
internal static class EingangsMail
{
    /// <summary>Anfang des Betreffs; danach folgt der Name der Freizeit.</summary>
    public const string BetreffAnfang = "Anmeldung eingegangen: ";

    public static string Betreff(string freizeit) => BetreffAnfang + freizeit;

    /// <summary>Die öffentliche Seite des Gast-Links (AD-11).</summary>
    public static string Link(string oeffentlicheAdresse, string token) => $"{oeffentlicheAdresse}/gast/{token}";

    /// <summary>Der letzte Kalendertag (Europe/Berlin), an dem der Gast-Link gilt.</summary>
    public static string BisTag(DateTimeOffset gueltigBis) =>
        FreizeitZeiten.Heute(gueltigBis.AddTicks(-1)).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    public static string Text(string oeffentlicheAdresse, string freizeit, string? vorname, string token, DateTimeOffset gueltigBis) =>
        $"""
        Anmeldung eingegangen: Die Leitung meldet sich in Kürze.

        Die Anmeldung {(string.IsNullOrWhiteSpace(vorname) ? "" : $"für {vorname.Trim()} ")}zur Freizeit „{freizeit}“ ist eingegangen. Bestätigung oder Absage kommen an diese Mail-Adresse.

        Mit diesem Gast-Link lässt sich der Stand der Anmeldung ansehen, auch ohne Konto:

        {Link(oeffentlicheAdresse, token)}

        Der Gast-Link gilt bis {BisTag(gueltigBis)}. Über ihn lässt sich auch ein Konto aus den Angaben erstellen.
        """;

    /// <summary>Schickt die Mail; Mail-Adresse, Name und Token erscheinen nie in Logs (AD-22).</summary>
    public static Task VerschickenAsync(
        IMailVersand mail, IConfiguration konfiguration, string an, string freizeit, string? vorname, string token, DateTimeOffset gueltigBis, CancellationToken abbruch) =>
        mail.SendenAsync(an, Betreff(freizeit), Text(OeffentlicheAdresse.Lesen(konfiguration), freizeit, vorname, token, gueltigBis), abbruch);
}
