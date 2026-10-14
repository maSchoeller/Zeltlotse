namespace Zeltlotse.Api.Contracts.Inventar;

/// <summary>
/// Story 3-3: Antwort von <c>GET /api/organisationen/{orgId}/inventar</c> (Org-Admin und Org-Überblick, FR-37): alle Artikel der Organisation,
/// sortiert nach Bezeichnung, je Artikel alle Reservierungen aller Freizeiten, sortiert nach <see cref="Reservierung.Von"/>.
/// </summary>
public sealed record OrgInventar(IReadOnlyList<InventarArtikel> Artikel);

/// <summary>
/// Story 3-3: Antwort von <c>GET /api/freizeiten/{freizeitId}/inventar</c> (Leitung und Team, FR-37): alle Artikel der Organisation, je Artikel
/// nur die Reservierungen dieser Freizeit. Erster und letzter Tag der Freizeit sind die Vorgabe für einen neuen Zeitraum.
/// </summary>
public sealed record FreizeitInventar(DateOnly ErsterTag, DateOnly LetzterTag, IReadOnlyList<InventarArtikel> Artikel);

/// <summary>Story 3-3: Ein Stück oder eine Sammlung im Inventar der Organisation (FR-37).</summary>
/// <param name="Version">Versionsnummer für den Autosave (AD-19).</param>
/// <param name="Menge">Freitext, z. B. „8 Stück“, oder <c>null</c>.</param>
/// <param name="Reservierungen">Im Org-Inventar alle, im Freizeit-Inventar nur die der eigenen Freizeit; sortiert nach <c>Von</c>.</param>
public sealed record InventarArtikel(Guid Id, uint Version, string Bezeichnung, string? Menge, IReadOnlyList<Reservierung> Reservierungen);

/// <summary>Story 3-3: Eine Reservierung eines Artikels für eine Freizeit und einen Zeitraum (beide Tage eingeschlossen).</summary>
/// <param name="Freizeit">Name der Freizeit.</param>
/// <param name="MaterialId">Das Material am Programmpunkt, für das reserviert ist, oder <c>null</c>.</param>
/// <param name="Konflikte">
/// Namen der anderen Freizeiten, die denselben Artikel zur gleichen Zeit reserviert haben, sortiert und ohne Doppelte. Zeltlotse verhindert und
/// löst nichts; die Leitungen sprechen sich ab.
/// </param>
public sealed record Reservierung(
    Guid Id,
    Guid FreizeitId,
    string Freizeit,
    DateOnly Von,
    DateOnly Bis,
    Guid? MaterialId,
    IReadOnlyList<string> Konflikte);

/// <summary>Story 3-3: Körper von <c>POST /api/organisationen/{orgId}/inventar</c>: nur die Bezeichnung ist nötig.</summary>
/// <param name="Bezeichnung">1 bis <see cref="InventarFelder.BezeichnungMaximaleLaenge"/> Zeichen; wird getrimmt.</param>
/// <param name="Menge">Höchstens <see cref="InventarFelder.MengeMaximaleLaenge"/> Zeichen; leer heißt ohne Menge.</param>
public sealed record InventarArtikelNeu(string? Bezeichnung, string? Menge);

/// <summary>
/// Story 3-3: Körper von <c>POST /api/freizeiten/{freizeitId}/inventar/{artikelId}/reservierungen</c>. Ohne Von und Bis gilt der Zeitraum der
/// Freizeit; nur eines von beiden oder Von nach Bis ist ein Fehler am Feld <c>bis</c>.
/// </summary>
/// <param name="MaterialId">Optional das Material am Programmpunkt, für das reserviert wird; Programm setzt dort dann den Status.</param>
public sealed record ReservierungNeu(DateOnly? Von, DateOnly? Bis, Guid? MaterialId);

/// <summary>Story 3-3: Bearbeitbare Felder eines Artikels für <c>PATCH /api/organisationen/{orgId}/inventar/{artikelId}</c> (AD-19).</summary>
public enum InventarArtikelFeld
{
    Bezeichnung,
    Menge,
}

/// <summary>
/// Story 3-3: JSON-Namen und Grenzen der Felder eines Artikels und einer Reservierung. <c>PATCH …/inventar/{artikelId}</c>: Körper
/// <c>{ "version": n, "alterWert": …, "bezeichnung"|"menge": neu }</c> mit genau einem Feld als Text (leere Menge = keine Menge), Antwort
/// <c>{ "version": n, "&lt;feld&gt;": gespeichert }</c>.
/// </summary>
public static class InventarFelder
{
    public const string Bezeichnung = "bezeichnung";

    public const string Menge = "menge";

    public const string Von = "von";

    public const string Bis = "bis";

    public const int BezeichnungMaximaleLaenge = 120;

    public const int MengeMaximaleLaenge = 40;

    public static string JsonName(InventarArtikelFeld feld) => feld switch
    {
        InventarArtikelFeld.Bezeichnung => Bezeichnung,
        InventarArtikelFeld.Menge => Menge,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };
}
