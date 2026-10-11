using Zeltlotse.Organisationen.Rollen;
using OrgRolleArt = Zeltlotse.SharedKernel.Rechte.OrgRolle;

namespace Zeltlotse.Organisationen.Einladungen;

/// <summary>
/// Text der Einladungs-Mail: Deutsch, ohne Anrede (EXPERIENCE.md → Voice and Tone), mit Name der Organisation, Rolle, Link
/// und Gültigkeit. Die Mail geht direkt nach dem Command aus dem Endpunkt (AD-13, Ausnahme „Einladungen“).
/// </summary>
internal static class EinladungsMail
{
    public const string Betreff = "Einladung zu Zeltlotse";

    /// <summary>Seite der Web-App, die die Einladung zeigt und erst beim Klick annimmt (AD-11).</summary>
    public static string Link(string oeffentlicheAdresse, string token) => $"{oeffentlicheAdresse}/app/einladung/{token}";

    public static string Text(string oeffentlicheAdresse, string organisation, OrgRolleArt rolle, string token) =>
        $"""
        Einladung in Zeltlotse für die Organisation „{organisation}“ mit der Rolle „{OrgRechte.Text(rolle)}“.

        Mit diesem Link geht es zur Einladung:

        {Link(oeffentlicheAdresse, token)}

        Der Link gilt 7 Tage und nur einmal. Zum Annehmen mit dieser Mail-Adresse anmelden.
        """;
}
