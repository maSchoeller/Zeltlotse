using System.Globalization;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Ui.Komponenten;
using ApiPhase = Zeltlotse.Api.Contracts.Gemeinsam.Phase;

namespace Zeltlotse.Ui.Texte;

/// <summary>Einheitliche Texte der Oberfläche für Werte aus der API (EXPERIENCE.md → Voice and Tone, Konvention „Zeit“).</summary>
public static class Anzeige
{
    private static readonly CultureInfo Deutsch = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>„Org-Admin“ bzw. „Org-Überblick“.</summary>
    public static string Rolle(OrgRolle rolle) => rolle switch
    {
        OrgRolle.OrgAdmin => "Org-Admin",
        OrgRolle.OrgUeberblick => "Org-Überblick",
        _ => rolle.ToString(),
    };

    /// <summary>Mehrere Rollen, durch Komma getrennt, z. B. „Org-Admin, Org-Überblick“.</summary>
    public static string Rollen(IEnumerable<OrgRolle> rollen) => string.Join(", ", rollen.Order().Select(Rolle));

    /// <summary>„Leitung“ bzw. „Team“.</summary>
    public static string Rolle(FreizeitRolle rolle) => rolle switch
    {
        FreizeitRolle.Leitung => "Leitung",
        FreizeitRolle.Team => "Team",
        _ => rolle.ToString(),
    };

    /// <summary>Mehrere Freizeit-Rollen, durch Komma getrennt, z. B. „Leitung, Team“.</summary>
    public static string Rollen(IEnumerable<FreizeitRolle> rollen) => string.Join(", ", rollen.Order().Select(Rolle));

    /// <summary>Phase einer Freizeit (AD-17) als Wort: „in Vorbereitung“, „läuft“, „Nachbereitung“, „gelaufen“.</summary>
    public static string Phase(Phase phase) => phase switch
    {
        ApiPhase.InVorbereitung => "in Vorbereitung",
        ApiPhase.Laeuft => "läuft",
        ApiPhase.Nachbereitung => "Nachbereitung",
        ApiPhase.Gelaufen => "gelaufen",
        _ => phase.ToString(),
    };

    /// <summary>Statusfarbe der Phase (DESIGN.md, Statuszuordnung); das Wort steht immer daneben.</summary>
    public static StatusArt PhaseArt(Phase phase) => phase switch
    {
        ApiPhase.Laeuft => StatusArt.Aktiv,
        ApiPhase.Nachbereitung => StatusArt.Warnung,
        ApiPhase.Gelaufen => StatusArt.Erfolg,
        _ => StatusArt.Offen,
    };

    /// <summary>Zeitraum als „TT.MM.JJJJ – TT.MM.JJJJ“.</summary>
    public static string Zeitraum(DateOnly ersterTag, DateOnly letzterTag) => $"{Datum(ersterTag)} – {Datum(letzterTag)}";

    /// <summary>Tag ohne Uhrzeit als TT.MM.JJJJ.</summary>
    public static string Datum(DateOnly tag) => tag.ToString("dd.MM.yyyy", Deutsch);

    /// <summary>Datum in Europe/Berlin als TT.MM.JJJJ.</summary>
    public static string Datum(DateTimeOffset zeitpunkt) =>
        TimeZoneInfo.ConvertTime(zeitpunkt, Berlin()).ToString("dd.MM.yyyy", Deutsch);

    /// <summary>Anzeigetext einer Nachweisart: „Führungszeugnis“, „JuLeiCa“, „Erste Hilfe“, „Präventionsschulung“.</summary>
    public static string Nachweis(Zeltlotse.Api.Contracts.Personen.NachweisArt art) => Nachweis((int)art);

    /// <inheritdoc cref="Nachweis(Zeltlotse.Api.Contracts.Personen.NachweisArt)"/>
    public static string Nachweis(Zeltlotse.Api.Contracts.Organisationen.NachweisArt art) => Nachweis((int)art);

    private static string Nachweis(int art) => art switch
    {
        1 => "Führungszeugnis",
        2 => "JuLeiCa",
        3 => "Erste Hilfe",
        4 => "Präventionsschulung",
        _ => art.ToString(Deutsch),
    };

    /// <summary>„Vorname Nachname“; ohne beide „Name fehlt“.</summary>
    public static string Name(string? vorname, string? nachname)
    {
        var name = string.Join(' ', new[] { vorname, nachname }.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim()));
        return name.Length == 0 ? "Name fehlt" : name;
    }

    private static TimeZoneInfo Berlin()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Local;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }
}
