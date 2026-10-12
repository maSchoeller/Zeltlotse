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
public sealed record PersonenEintrag(string Initialen, string Name, string? Zeile);
