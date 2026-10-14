using System.Text.Json.Serialization;

namespace Zeltlotse.Api.Contracts.Programm;

/// <summary>
/// Story 3-2: Zustand eines Materials am Programmpunkt (FR-36); im JSON als Name. Jeder Zustand lässt sich direkt setzen (Segmente), ein neues
/// Material ist <see cref="Gebraucht"/>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<MaterialStatus>))]
public enum MaterialStatus
{
    /// <summary>Wird gebraucht, ist aber noch nicht da (Vorgabe beim Anlegen).</summary>
    Gebraucht = 1,

    /// <summary>Ist schon vorhanden.</summary>
    Vorhanden = 2,

    /// <summary>Ist besorgt.</summary>
    Besorgt = 3,

    /// <summary>Ist eingepackt.</summary>
    Eingepackt = 4,
}

/// <summary>Story 3-2: Ein Material an einem Programmpunkt (FR-36, FR-69), mit Titel, Tag und Beginn seines Programmpunkts.</summary>
/// <param name="Version">Versionsnummer für den Autosave (AD-19).</param>
/// <param name="ProgrammpunktId">Der Programmpunkt, zu dem das Material gehört.</param>
/// <param name="Programmpunkt">Titel des Programmpunkts.</param>
/// <param name="Tag">Tag des Programmpunkts.</param>
/// <param name="Beginn">Uhrzeit des Programmpunkts als <c>HH:mm</c> oder <c>null</c>.</param>
/// <param name="Menge">Freitext, z. B. „4 × 10 m“, oder <c>null</c>.</param>
/// <param name="BringtMit">Die Person, die das Material mitbringt („Ich bringe das mit“); den Namen setzt der Client zusammen (AD-2).</param>
/// <param name="Meins">Die angemeldete Person bringt das Material mit.</param>
/// <param name="ReservierungId">
/// Story 3-3: Die Reservierung im Inventar, über die das Material bereitliegt (Ereignis <c>ReservierungGeaendert</c>), sonst <c>null</c>.
/// </param>
public sealed record Material(
    Guid Id,
    uint Version,
    Guid ProgrammpunktId,
    string Programmpunkt,
    DateOnly Tag,
    string? Beginn,
    string Bezeichnung,
    string? Menge,
    MaterialStatus Status,
    Guid? BringtMit,
    bool Meins,
    Guid? ReservierungId = null);

/// <summary>
/// Story 3-2: Antwort von <c>GET /api/freizeiten/{freizeitId}/programm/material</c> (Leitung und Team, FR-69): das Material aller Programmpunkte,
/// sortiert wie der Wochenraster (Tag, Beginn ohne Uhrzeit zuletzt, Titel) und dann nach Bezeichnung.
/// </summary>
public sealed record Materialliste(IReadOnlyList<Material> Material);

/// <summary>Story 3-2: Körper von <c>POST …/programm/punkte/{punktId}/material</c>: nur die Bezeichnung ist nötig.</summary>
/// <param name="Bezeichnung">1 bis <see cref="MaterialFelder.BezeichnungMaximaleLaenge"/> Zeichen; wird getrimmt.</param>
/// <param name="Menge">Höchstens <see cref="MaterialFelder.MengeMaximaleLaenge"/> Zeichen; leer heißt ohne Menge.</param>
public sealed record MaterialNeu(string? Bezeichnung, string? Menge);

/// <summary>Story 3-2: Körper von <c>POST …/programm/material/{materialId}/status</c>: der neue Zustand.</summary>
public sealed record MaterialStatusNeu(MaterialStatus? Status);

/// <summary>Story 3-2: Bearbeitbare Felder eines Materials für <c>PATCH …/programm/material/{materialId}</c> (AD-19); der Status ist ein Übergang.</summary>
public enum MaterialFeld
{
    Bezeichnung,
    Menge,
}

/// <summary>
/// Story 3-2: JSON-Namen und Grenzen der Felder eines Materials. <c>PATCH …/programm/material/{materialId}</c>: Körper
/// <c>{ "version": n, "alterWert": …, "bezeichnung"|"menge": neu }</c> mit genau einem Feld als Text (leere Menge = keine Menge), Antwort
/// <c>{ "version": n, "&lt;feld&gt;": gespeichert }</c>.
/// </summary>
public static class MaterialFelder
{
    public const string Bezeichnung = "bezeichnung";

    public const string Menge = "menge";

    public const string Status = "status";

    public const int BezeichnungMaximaleLaenge = 120;

    public const int MengeMaximaleLaenge = 40;

    public static string JsonName(MaterialFeld feld) => feld switch
    {
        MaterialFeld.Bezeichnung => Bezeichnung,
        MaterialFeld.Menge => Menge,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };
}
