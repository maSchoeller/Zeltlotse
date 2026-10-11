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
