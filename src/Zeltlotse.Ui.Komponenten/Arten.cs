using Microsoft.AspNetCore.Components;

namespace Zeltlotse.Ui.Komponenten;

/// <summary>Art eines <see cref="Knopf"/>: Standard (Hauptaktion), Outline (Nebenaktion), Text (tertiär).</summary>
public enum KnopfArt
{
    Standard,
    Outline,
    Text,
}

/// <summary>Art eines <see cref="Status"/> nach der Statuszuordnung in DESIGN.md.</summary>
public enum StatusArt
{
    Offen,
    Aktiv,
    Erfolg,
    Warnung,
    Fehler,
}

/// <summary>Art einer <see cref="Meldung"/>; Fehler werden mit <c>role="alert"</c> angesagt, alle anderen mit <c>role="status"</c>.</summary>
public enum MeldungArt
{
    Hinweis,
    Erfolg,
    Warnung,
    Fehler,
}

/// <summary>Art eines <see cref="Balken"/>: Füllung im Akzent, in Warnung oder in Fehler.</summary>
public enum BalkenArt
{
    Normal,
    Warnung,
    Fehler,
}

/// <summary>Zustand eines <see cref="SpeicherHinweis"/> am bearbeiteten Feld.</summary>
public enum SpeicherZustand
{
    Keiner,
    Gespeichert,
    Konflikt,
    Gescheitert,
}

/// <summary>Ein Punkt der Seiten- oder unteren Navigation.</summary>
/// <param name="Text">Beschriftung, nur Text.</param>
/// <param name="Href">Ziel des Links.</param>
/// <param name="Aktiv">Aktueller Bereich: setzt <c>aria-current="page"</c>.</param>
/// <param name="Anzahl">Optionale Anzahl, als reiner Text rechts (nur Seitennavigation).</param>
public sealed record NavPunkt(string Text, string Href, bool Aktiv = false, int? Anzahl = null);

/// <summary>Eine Gruppe der Seitennavigation mit Dachzeile.</summary>
public sealed record NavGruppe(string Dachzeile, IReadOnlyList<NavPunkt> Punkte);

/// <summary>Eine Spalte einer <see cref="Datentabelle{TZeile}"/>: Titel (Spaltenkopf und Bezeichnung in der Handy-Ansicht) und Inhalt je Zeile.</summary>
public sealed record Spalte<TZeile>(string Titel, RenderFragment<TZeile> Inhalt);

/// <summary>Eine Option einer <see cref="Auswahl"/>: gespeicherter Wert und sichtbarer Text.</summary>
public sealed record AuswahlOption(string Wert, string Text);

/// <summary>Ein Eintrag einer <see cref="PersonenListe"/>: Initialen für den Avatar, Name und eine optionale Zeile darunter.</summary>
/// <param name="Href">Story 2-8: optionales Ziel; dann ist der Name ein Link.</param>
public sealed record PersonenEintrag(string Initialen, string Name, string? Zeile, string? Href = null);

/// <summary>Ein Eintrag einer <see cref="Fehleruebersicht"/>: Kennung des Feldes (Ziel des Sprunglinks) und der Fehlertext ohne Präfix.</summary>
public sealed record FeldFehler(string FeldId, string Text);

/// <summary>Eine Zeile einer <see cref="StatusListe"/>: Text, Status-Wort und seine Art.</summary>
/// <param name="Darunter">Optional eine Zeile unter dem Eintrag, z. B. der Kontakt für Absagen (statisch tauglich).</param>
public sealed record StatusEintrag(string Text, string Wort, StatusArt Art, RenderFragment? Darunter = null);

/// <summary>Eine Zeile einer <see cref="DetailsListe"/>: Begriff (z. B. eine Frage) und Wert (z. B. die Antwort).</summary>
public sealed record DetailsEintrag(string Begriff, string Wert);

/// <summary>Story 3-1: Ein Reiter einer <see cref="Reiter"/>-Leiste: Beschriftung, Ziel und ob er die gewählte Ansicht ist (<c>aria-current</c>).</summary>
public sealed record ReiterEintrag(string Text, string Href, bool Aktiv = false);

/// <summary>Story 3-1: Eine Spalte der <see cref="TagesSpalten"/>: Tageskopf (z. B. „Mo 02.08.“) und Inhalt (z. B. die Termine des Tages).</summary>
public sealed record TagesSpalte(string Kopf, RenderFragment Inhalt);

/// <summary>
/// Story 4-2: Eine Zeile eines <see cref="Vergleich"/>: Text (z. B. „Mehl“) und optional die Menge davor (z. B. „1 kg“); geänderte Zeilen sind fett
/// und werden als „(geändert)“ vorgelesen.
/// </summary>
public sealed record VergleichZeile(string Text, string? Menge, bool Geaendert);
