using System.Globalization;
using Zeltlotse.Api.Contracts.Gemeinsam;
using MaterialZustand = Zeltlotse.Api.Contracts.Programm.MaterialStatus;
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

    /// <summary>Story 2-10: Datum und Uhrzeit in Europe/Berlin als „TT.MM.JJJJ HH:mm“.</summary>
    public static string DatumUhrzeit(DateTimeOffset zeitpunkt) =>
        TimeZoneInfo.ConvertTime(zeitpunkt, Berlin()).ToString("dd.MM.yyyy HH:mm", Deutsch);

    /// <summary>Story 2-10: die Art eines Eintrags im Vorfall-Protokoll, wie Protokoll, Vorfall-Notizen und Archiv sie nennen.</summary>
    public static string VorfallArtText(Zeltlotse.Api.Contracts.Sicherheit.VorfallArt art) => art switch
    {
        Zeltlotse.Api.Contracts.Sicherheit.VorfallArt.Geschehen => "Was passiert ist",
        Zeltlotse.Api.Contracts.Sicherheit.VorfallArt.Medikament => "Medikament gegeben",
        Zeltlotse.Api.Contracts.Sicherheit.VorfallArt.Entnahme => "Aus dem Erste-Hilfe-Kasten entnommen",
        _ => throw new ArgumentOutOfRangeException(nameof(art), art, null),
    };

    /// <summary>Story 3-4: die Art eines Eintrags auf der Schadens- und Verlustliste, wie Freizeit-Seite und Anschaffungswünsche sie nennen.</summary>
    public static string SchadenArt(Zeltlotse.Api.Contracts.Inventar.SchadenArt art) => art switch
    {
        Zeltlotse.Api.Contracts.Inventar.SchadenArt.Schaden => "Schaden",
        Zeltlotse.Api.Contracts.Inventar.SchadenArt.Verlust => "Verlust",
        Zeltlotse.Api.Contracts.Inventar.SchadenArt.Entnahme => "Erste-Hilfe-Entnahme",
        _ => throw new ArgumentOutOfRangeException(nameof(art), art, null),
    };

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
        Zeltlotse.Api.Contracts.Personen.ZugriffZweck.AllergeneFuerKueche => "Allergen-Abgleich der Küche",
        Zeltlotse.Api.Contracts.Personen.ZugriffZweck.SonderkostFuerKueche => "Sonderkost-Abgleich der Küche",
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

    /// <summary>Story 2-7: „Team“, „Eltern und Teilnehmende“ bzw. „Öffentlich“.</summary>
    public static string Sichtbarkeit(Zeltlotse.Api.Contracts.Freizeiten.DokumentSichtbarkeit sichtbarkeit) => sichtbarkeit switch
    {
        Zeltlotse.Api.Contracts.Freizeiten.DokumentSichtbarkeit.Team => "Team",
        Zeltlotse.Api.Contracts.Freizeiten.DokumentSichtbarkeit.ElternUndTeilnehmende => "Eltern und Teilnehmende",
        Zeltlotse.Api.Contracts.Freizeiten.DokumentSichtbarkeit.Oeffentlich => "Öffentlich",
        _ => sichtbarkeit.ToString(),
    };

    /// <summary>Story 2-7: Herkunft eines Dokuments der Freizeit: „Organisation“ (eingebunden) oder „Freizeit“.</summary>
    public static string Herkunft(bool ausOrganisation) => ausOrganisation ? "Organisation" : "Freizeit";

    /// <summary>Story 2-7: ein Dokument als Link-Text der Elterninfo, z. B. „Packliste (PDF)“.</summary>
    public static string Dokument(string titel, string dateiart) => $"{titel} ({dateiart})";

    /// <summary>Story 2-7: Trifft der Titel die Suche? Enthält, ohne Groß- und Kleinschreibung; eine leere Suche trifft alles.</summary>
    public static bool TitelTrifft(string titel, string? suche) =>
        string.IsNullOrWhiteSpace(suche) || Deutsch.CompareInfo.IndexOf(titel, suche.Trim(), CompareOptions.IgnoreCase) >= 0;

    /// <summary>Story 2-8: Ziel eines Telefon-Links: „tel:“ plus nur „+“ und Ziffern (dieselbe Regel wie <see cref="TelefonLink"/>).</summary>
    public static string TelefonHref(string? nummer) => TelefonLink.Href(nummer);

    /// <summary>Story 2-8: Initialen für den Avatar aus Vor- und Nachname, z. B. „EM“; ohne beide „?“.</summary>
    public static string Initialen(string? vorname, string? nachname)
    {
        var initialen = string.Concat(new[] { vorname, nachname }.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => char.ToUpper(t!.Trim()[0], Deutsch)));
        return initialen.Length == 0 ? "?" : initialen;
    }

    /// <summary>„Vorname Nachname“; ohne beide „Name fehlt“.</summary>
    public static string Name(string? vorname, string? nachname)
    {
        var name = string.Join(' ', new[] { vorname, nachname }.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim()));
        return name.Length == 0 ? "Name fehlt" : name;
    }

    /// <summary>Story 3-1: Tag im Reiter und im Spaltenkopf des Wochenrasters, z. B. „Mo 02.08.“.</summary>
    public static string TagKurz(DateOnly tag) => $"{tag.ToString("ddd", Deutsch).TrimEnd('.')} {tag.ToString("dd.MM.", Deutsch)}";

    /// <summary>Story 3-1: Tag in Worten, z. B. „Montag, 2. August 2027“.</summary>
    public static string TagLang(DateOnly tag) => tag.ToString("dddd, d. MMMM yyyy", Deutsch);

    /// <summary>Story 3-1: Status eines übernommenen Programmpunkts: „Übernommen: Lea Feuerstein“, ohne bekannten Namen nur „Übernommen“.</summary>
    public static string Uebernommen(string? name) => string.IsNullOrWhiteSpace(name) ? "Übernommen" : $"Übernommen: {name}";

    /// <summary>Story 3-2: Wer ein Material mitbringt: „Bringt mit: Miriam Zeltner“, ohne bekannten Namen „Bringt jemand mit“.</summary>
    public static string BringtMit(string? name) => string.IsNullOrWhiteSpace(name) ? "Bringt jemand mit" : $"Bringt mit: {name}";

    /// <summary>Story 3-2: Status eines Materials als Wort: „gebraucht“, „vorhanden“, „besorgt“, „eingepackt“.</summary>
    public static string MaterialStatus(MaterialZustand status) => status switch
    {
        MaterialZustand.Gebraucht => "gebraucht",
        MaterialZustand.Vorhanden => "vorhanden",
        MaterialZustand.Besorgt => "besorgt",
        MaterialZustand.Eingepackt => "eingepackt",
        _ => status.ToString(),
    };

    /// <summary>Story 3-2: Farbe des Material-Status (EXPERIENCE.md → Status): gebraucht = offen, vorhanden und besorgt = aktiv, eingepackt = Erfolg.</summary>
    public static StatusArt MaterialStatusArt(MaterialZustand status) => status switch
    {
        MaterialZustand.Vorhanden or MaterialZustand.Besorgt => StatusArt.Aktiv,
        MaterialZustand.Eingepackt => StatusArt.Erfolg,
        _ => StatusArt.Offen,
    };

    /// <summary>
    /// Story 3-3: kurzer Zeitraum einer Reservierung: „02.08.–13.08.2027“; über den Jahreswechsel „30.12.2027–02.01.2028“, ein einzelner Tag
    /// „03.08.2027“. (<see cref="Zeitraum"/> bleibt die lange Form mit beiden Jahren.)
    /// </summary>
    public static string ZeitraumKurz(DateOnly von, DateOnly bis)
    {
        if (von == bis)
        {
            return Datum(von);
        }

        return von.Year == bis.Year ? $"{von.ToString("dd.MM.", Deutsch)}–{Datum(bis)}" : $"{Datum(von)}–{Datum(bis)}";
    }

    /// <summary>
    /// Story 3-3: Konflikthinweis einer Reservierung (EXPERIENCE.md → State Patterns „Inventar-Konflikt“): „Zur gleichen Zeit auch für
    /// Kinderbibeltage 2027 reserviert. Absprache mit der anderen Leitung nötig.“; mehrere Freizeiten mit Komma und „und“.
    /// </summary>
    public static string Konflikt(IReadOnlyList<string> freizeiten) =>
        $"Zur gleichen Zeit auch für {Aufzaehlung(freizeiten)} reserviert. Absprache mit der anderen Leitung nötig.";

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

    /// <summary>Story 3-5: Phase einer Aufgabe: „Vorbereitung“, „Während der Freizeit“, „Nachbereitung“.</summary>
    public static string AufgabenPhase(Zeltlotse.Api.Contracts.Programm.AufgabenPhase phase) => phase switch
    {
        Zeltlotse.Api.Contracts.Programm.AufgabenPhase.Vorbereitung => "Vorbereitung",
        Zeltlotse.Api.Contracts.Programm.AufgabenPhase.Waehrend => "Während der Freizeit",
        Zeltlotse.Api.Contracts.Programm.AufgabenPhase.Nachbereitung => "Nachbereitung",
        _ => phase.ToString(),
    };

    /// <summary>
    /// Story 3-8: Satz der Inline-Bestätigung „Freizeit abschließen“: ohne offene Aufgaben „Freizeit abschließen? Danach ist sie nur lesbar.“,
    /// sonst „1 Aufgabe offen – trotzdem abschließen?“ bzw. „2 Aufgaben offen – trotzdem abschließen?“.
    /// </summary>
    public static string AbschlussSatz(int offeneAufgaben) => offeneAufgaben switch
    {
        <= 0 => "Freizeit abschließen? Danach ist sie nur lesbar.",
        1 => "1 Aufgabe offen – trotzdem abschließen?",
        _ => $"{offeneAufgaben.ToString(Deutsch)} Aufgaben offen – trotzdem abschließen?",
    };

    /// <summary>Story 3-5: Warnhinweis im Cockpit: „1 Aufgabe ist noch nicht übernommen“, „3 Aufgaben sind noch nicht übernommen“.</summary>
    public static string AufgabenUnbesetzt(int anzahl) =>
        anzahl == 1 ? "1 Aufgabe ist noch nicht übernommen" : $"{anzahl.ToString(Deutsch)} Aufgaben sind noch nicht übernommen";

    /// <summary>Story 3-6: „Abgelegt am 14.10.2026“ (Europe/Berlin) an einem Eintrag der Programmbibliothek.</summary>
    public static string AbgelegtAm(DateTimeOffset zeitpunkt) => $"Abgelegt am {Datum(zeitpunkt)}";

    /// <summary>Story 3-6: ein Material im Bibliothekseintrag: „Seile · 4 × 10 m“, ohne Menge nur die Bezeichnung.</summary>
    public static string BibliotheksMaterial(Zeltlotse.Api.Contracts.Programm.BibliotheksMaterial material) =>
        string.IsNullOrWhiteSpace(material?.Menge) ? material?.Bezeichnung ?? "" : $"{material.Bezeichnung} · {material.Menge}";

    /// <summary>Story 3-6: eine Aufgabe im Bibliothekseintrag: „Fahnen verstecken · Während der Freizeit“.</summary>
    public static string BibliotheksAufgabe(Zeltlotse.Api.Contracts.Programm.BibliotheksAufgabe aufgabe) =>
        aufgabe is null ? "" : $"{aufgabe.Titel} · {AufgabenPhase(aufgabe.Phase)}";

    /// <summary>Story 4-1: „für 4 Personen“, „für 1 Person“.</summary>
    public static string FuerPersonen(int anzahl) => anzahl == 1 ? "für 1 Person" : $"für {anzahl} Personen";

    /// <summary>Story 4-1: eine Menge deutsch ohne Null-Nachkommastellen, z. B. „2,25“ oder „6000“; ohne Menge leer.</summary>
    public static string Menge(decimal? menge) => menge is { } wert ? Zeltlotse.Api.Contracts.Kueche.ZutatFelder.ZahlText(wert) : "";

    /// <summary>Story 4-1: die Allergene einer Komma-Liste als Text, z. B. „Glutenhaltiges Getreide, Milch (inkl. Laktose)“; ohne Allergene „keine angegeben“ (heißt nicht „frei von Allergenen“).</summary>
    public static string Allergene(string? liste) => Zeltlotse.Api.Contracts.Kueche.Allergene.Texte(liste) is { Count: > 0 } texte ? string.Join(", ", texte) : "keine angegeben";
}
