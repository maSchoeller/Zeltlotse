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

    /// <summary>
    /// Zeitraum in Worten für die öffentlichen Seiten: „2. bis 13. August 2027“, „30. Juli bis 2. August 2027“,
    /// „30. Dezember 2027 bis 2. Januar 2028“; ein einzelner Tag „2. August 2027“.
    /// </summary>
    public static string ZeitraumLang(DateOnly ersterTag, DateOnly letzterTag)
    {
        string Tag(DateOnly tag) => tag.ToString("d.", Deutsch);
        string Monat(DateOnly tag) => tag.ToString("MMMM", Deutsch);
        string Jahr(DateOnly tag) => tag.ToString("yyyy", Deutsch);

        if (ersterTag == letzterTag)
        {
            return $"{Tag(ersterTag)} {Monat(ersterTag)} {Jahr(ersterTag)}";
        }

        if (ersterTag.Year != letzterTag.Year)
        {
            return $"{Tag(ersterTag)} {Monat(ersterTag)} {Jahr(ersterTag)} bis {Tag(letzterTag)} {Monat(letzterTag)} {Jahr(letzterTag)}";
        }

        return ersterTag.Month == letzterTag.Month
            ? $"{Tag(ersterTag)} bis {Tag(letzterTag)} {Monat(letzterTag)} {Jahr(letzterTag)}"
            : $"{Tag(ersterTag)} {Monat(ersterTag)} bis {Tag(letzterTag)} {Monat(letzterTag)} {Jahr(letzterTag)}";
    }

    /// <summary>Zeile einer Person des Teams auf der Freizeitseite: „Leitung · aus Musterstadt“ bzw. nur „Leitung“.</summary>
    public static string TeamZeile(FreizeitRolle rolle, string? herkunft) =>
        string.IsNullOrWhiteSpace(herkunft) ? Rolle(rolle) : $"{Rolle(rolle)} · aus {herkunft.Trim()}";

    /// <summary>„1 Monat“, „12 Monate“.</summary>
    public static string Monate(int anzahl) => anzahl == 1 ? "1 Monat" : $"{anzahl.ToString(Deutsch)} Monate";

    /// <summary>„1 Tag“, „30 Tage“.</summary>
    public static string Tage(int anzahl) => anzahl == 1 ? "1 Tag" : $"{anzahl.ToString(Deutsch)} Tage";

    /// <summary>Aufzählung mit Komma und „und“: „Führungszeugnis, JuLeiCa und Erste Hilfe“.</summary>
    public static string Aufzaehlung(IReadOnlyList<string> teile) => teile.Count switch
    {
        0 => "",
        1 => teile[0],
        _ => $"{string.Join(", ", teile.Take(teile.Count - 1))} und {teile[^1]}",
    };

    /// <summary>Tag ohne Uhrzeit als TT.MM.JJJJ.</summary>
    public static string Datum(DateOnly tag) => tag.ToString("dd.MM.yyyy", Deutsch);

    /// <summary>Datum in Europe/Berlin als TT.MM.JJJJ.</summary>
    public static string Datum(DateTimeOffset zeitpunkt) =>
        TimeZoneInfo.ConvertTime(zeitpunkt, Berlin()).ToString("dd.MM.yyyy", Deutsch);

    /// <summary>Der Kalendertag eines Zeitpunkts in Europe/Berlin.</summary>
    public static DateOnly Tag(DateTimeOffset zeitpunkt) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(zeitpunkt, Berlin()).DateTime);

    /// <summary>
    /// Der letzte Tag, an dem etwas mit dem Ende <paramref name="ende"/> noch gilt, als TT.MM.JJJJ: Ein Ende um 00:00 Uhr gilt bis
    /// einschließlich des Vortags (z. B. das Ende des Freigabefensters).
    /// </summary>
    public static string BisEinschliesslich(DateTimeOffset ende) => Datum(Tag(ende.AddTicks(-1)));

    /// <summary>
    /// Story 2-5: Bis wann die Freizeit Einsicht in den Freizeitpass hat: „Einsicht bis TT.MM.JJJJ“ (letzter Tag, an dem sie noch gilt) bzw.
    /// „Einsicht beendet am TT.MM.JJJJ“, wenn <paramref name="bis"/> vorbei ist. Beide nennen denselben Tag: den letzten, an dem die
    /// Einsicht noch galt (ein Ende um 00:00 Uhr gehört zum Vortag).
    /// </summary>
    public static string Einsicht(DateTimeOffset bis, DateTimeOffset jetzt) =>
        bis <= jetzt ? $"Einsicht beendet am {BisEinschliesslich(bis)}" : $"Einsicht bis {BisEinschliesslich(bis)}";

    /// <summary>
    /// Story 2-5: eine Antwort im Freizeitpass zum Lesen: Auswahl als Texte der Optionen (mit Komma, „Sonstiges“ dahinter), Ja/Nein als
    /// „Ja“/„Nein“, ein Datum als TT.MM.JJJJ, Freitext wie eingegeben; ohne Angabe „keine Angabe“.
    /// </summary>
    public static string Antwort(Zeltlotse.Api.Contracts.Personen.FreizeitpassFrage frage)
    {
        ArgumentNullException.ThrowIfNull(frage);
        var wert = string.IsNullOrWhiteSpace(frage.Wert) ? null : frage.Wert.Trim();
        var teile = new List<string>();
        if (wert is not null)
        {
            switch (frage.Art)
            {
                case Zeltlotse.Api.Contracts.Personen.FrageArt.Auswahl:
                case Zeltlotse.Api.Contracts.Personen.FrageArt.JaNein:
                    var schluessel = wert.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    teile.AddRange(schluessel.Select(s => frage.Optionen.FirstOrDefault(o => o.Wert == s)?.Text ?? s));
                    break;
                case Zeltlotse.Api.Contracts.Personen.FrageArt.Datum:
                    teile.Add(DateOnly.TryParseExact(wert, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var tag) ? Datum(tag) : wert);
                    break;
                default:
                    teile.Add(wert);
                    break;
            }
        }

        if (!string.IsNullOrWhiteSpace(frage.Sonstiges))
        {
            teile.Add(frage.Sonstiges.Trim());
        }

        return teile.Count == 0 ? "keine Angabe" : string.Join(", ", teile);
    }

    /// <summary>Status einer Anmeldung als Wort (EXPERIENCE.md → State Patterns): „Eingegangen“, „Bestätigt“, „Abgesagt“.</summary>
    public static string Status(Zeltlotse.Api.Contracts.Freizeiten.AnmeldungStatus status) => status switch
    {
        Zeltlotse.Api.Contracts.Freizeiten.AnmeldungStatus.Eingegangen => "Eingegangen",
        Zeltlotse.Api.Contracts.Freizeiten.AnmeldungStatus.Bestaetigt => "Bestätigt",
        Zeltlotse.Api.Contracts.Freizeiten.AnmeldungStatus.Abgesagt => "Abgesagt",
        _ => status.ToString(),
    };

    /// <summary>Status-Art einer Anmeldung: eingegangen = offen, bestätigt = Erfolg, abgesagt = Fehler.</summary>
    public static StatusArt AnmeldungStatusArt(Zeltlotse.Api.Contracts.Freizeiten.AnmeldungStatus status) => status switch
    {
        Zeltlotse.Api.Contracts.Freizeiten.AnmeldungStatus.Eingegangen => StatusArt.Offen,
        Zeltlotse.Api.Contracts.Freizeiten.AnmeldungStatus.Bestaetigt => StatusArt.Erfolg,
        Zeltlotse.Api.Contracts.Freizeiten.AnmeldungStatus.Abgesagt => StatusArt.Fehler,
        _ => StatusArt.Aktiv,
    };

    /// <summary>Wofür jemand Einsicht hatte (Zugriffsprotokoll): „Nachweise eingesehen“, „Freizeitpass eingesehen“.</summary>
    public static string Zweck(Zeltlotse.Api.Contracts.Personen.ZugriffZweck zweck) => zweck switch
    {
        Zeltlotse.Api.Contracts.Personen.ZugriffZweck.NachweiseEingesehen => "Nachweise eingesehen",
        Zeltlotse.Api.Contracts.Personen.ZugriffZweck.FreizeitpassEingesehen => "Freizeitpass eingesehen",
        _ => zweck.ToString(),
    };

    /// <summary>Anzeigetext einer Nachweisart: „Führungszeugnis“, „JuLeiCa“, „Erste Hilfe“, „Präventionsschulung“.</summary>
    public static string Nachweis(Zeltlotse.Api.Contracts.Personen.NachweisArt art) => Nachweis((int)art);

    /// <inheritdoc cref="Nachweis(Zeltlotse.Api.Contracts.Personen.NachweisArt)"/>
    public static string Nachweis(Zeltlotse.Api.Contracts.Organisationen.NachweisArt art) => Nachweis((int)art);

    /// <inheritdoc cref="Nachweis(Zeltlotse.Api.Contracts.Personen.NachweisArt)"/>
    public static string Nachweis(Zeltlotse.Api.Contracts.Freizeiten.NachweisArt art) => Nachweis((int)art);

    /// <summary>
    /// Ein Warnhinweis zu einem Pflicht-Nachweis (FR-11): „Führungszeugnis nicht bestätigt“, „Erste Hilfe läuft am TT.MM.JJJJ ab“
    /// bzw. „Erste Hilfe abgelaufen am TT.MM.JJJJ“.
    /// </summary>
    public static string Warnung(Zeltlotse.Api.Contracts.Freizeiten.NachweisWarnung warnung) => warnung switch
    {
        { Grund: Zeltlotse.Api.Contracts.Freizeiten.NachweisWarnungGrund.LaeuftAb, GueltigBis: { } bis } => $"{Nachweis(warnung.Art)} läuft am {Datum(bis)} ab",
        { Grund: Zeltlotse.Api.Contracts.Freizeiten.NachweisWarnungGrund.Abgelaufen, GueltigBis: { } bis } => $"{Nachweis(warnung.Art)} abgelaufen am {Datum(bis)}",
        _ => $"{Nachweis(warnung.Art)} nicht bestätigt",
    };

    private static string Nachweis(int art) => art switch
    {
        1 => "Führungszeugnis",
        2 => "JuLeiCa",
        3 => "Erste Hilfe",
        4 => "Präventionsschulung",
        _ => art.ToString(Deutsch),
    };

    /// <summary>Anzahl mit Einzahl oder Mehrzahl: „1 Punkt“, „2 Punkte“.</summary>
    public static string Anzahl(int anzahl, string einzahl, string mehrzahl) => anzahl == 1 ? $"1 {einzahl}" : $"{anzahl.ToString(Deutsch)} {mehrzahl}";

    /// <summary>
    /// Stand eines Freizeitpasses im Leitungs-Cockpit (Story 2-6): „Freizeitpass fehlt noch“, wenn keine gefragte Angabe vollständig ist,
    /// sonst „5 von 8 Angaben offen“.
    /// </summary>
    public static string FreizeitpassStand(int gefragt, int offen) =>
        offen >= gefragt ? "Freizeitpass fehlt noch" : $"{offen.ToString(Deutsch)} von {gefragt.ToString(Deutsch)} Angaben offen";

    /// <summary>Warnzeile im Leitungs-Cockpit: „1 Freizeitpass unvollständig“, „4 Freizeitpässe unvollständig“.</summary>
    public static string FreizeitpaesseUnvollstaendig(int anzahl) => $"{Anzahl(anzahl, "Freizeitpass", "Freizeitpässe")} unvollständig";

    /// <summary>Text der Meldung über den Warnhinweisen: „1 Punkt ist offen.“, „2 Punkte sind offen.“</summary>
    public static string OffenePunkte(int anzahl) => anzahl == 1 ? "1 Punkt ist offen." : $"{anzahl.ToString(Deutsch)} Punkte sind offen.";

    /// <summary>Anmeldestand je Art: „Teilnehmende: 1 eingegangen, 1 bestätigt“.</summary>
    public static string Anmeldestand(string art, int eingegangen, int bestaetigt) =>
        $"{art}: {eingegangen.ToString(Deutsch)} eingegangen, {bestaetigt.ToString(Deutsch)} bestätigt";

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
