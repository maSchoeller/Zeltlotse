namespace Zeltlotse.Api.Contracts.Sicherheit;

/// <summary>
/// Story 2-9: Antwort von <c>GET /api/freizeiten/{freizeitId}/sicherheit/schutzkonzept</c> (Leitung und Team, FR-30): das Dokument der
/// Freizeit, das als Schutzkonzept gilt, und wer aus dem Team es als gelesen bestätigt hat. Ohne gespeichertes oder ohne noch vorhandenes
/// Dokument sind <see cref="DokumentId"/>, <see cref="Titel"/> und <see cref="Dateiart"/> <c>null</c>; ohne Zeile ist <see cref="Version"/>
/// <c>0</c>.
/// </summary>
/// <param name="DokumentId">Kennung des Dokuments der Freizeit (eigene Datei oder Einbindung eines Org-Dokuments).</param>
/// <param name="Titel">Titel des Dokuments.</param>
/// <param name="Dateiart">„PDF“, „JPEG“, „PNG“ oder „WebP“.</param>
/// <param name="SelbstGelesenAm">Wann die anfragende Person dieses Dokument als gelesen bestätigt hat, sonst <c>null</c>.</param>
/// <param name="Team">Alle mit Freizeit-Rolle Leitung oder Team, nach Nachname und Vorname.</param>
/// <param name="Version">Versionsnummer für den Autosave (AD-19).</param>
public sealed record Schutzkonzept(
    Guid? DokumentId,
    string? Titel,
    string? Dateiart,
    DateTimeOffset? SelbstGelesenAm,
    IReadOnlyList<SchutzkonzeptLeser> Team,
    uint Version);

/// <summary>Story 2-9: Eine Person des Teams und wann sie das aktuelle Schutzkonzept als gelesen bestätigt hat.</summary>
/// <param name="Leitung">Die Person ist Leitung der Freizeit (sonst Team).</param>
/// <param name="GelesenAm">Bestätigung des aktuellen Dokuments, sonst <c>null</c>.</param>
public sealed record SchutzkonzeptLeser(Guid PersonId, string? Vorname, string? Nachname, bool Leitung, DateTimeOffset? GelesenAm);

/// <summary>Story 2-9: Körper von <c>POST /api/freizeiten/{freizeitId}/sicherheit/schutzkonzept/gelesen</c>: das gelesene Dokument.</summary>
public sealed record SchutzkonzeptGelesen(Guid DokumentId);

/// <summary>Bearbeitbare Felder des Schutzkonzepts für <c>PATCH /api/freizeiten/{freizeitId}/sicherheit/schutzkonzept</c> (AD-19).</summary>
public enum SchutzkonzeptFeld
{
    DokumentId,
}

/// <summary>
/// JSON-Namen der Felder für <c>PATCH /api/freizeiten/{freizeitId}/sicherheit/schutzkonzept</c>: Körper
/// <c>{ "version": n, "alterWert": …, "dokumentId": neu }</c> mit der Kennung als Text oder <c>null</c> (kein Schutzkonzept), Antwort
/// <c>{ "version": n, "dokumentId": gespeichert }</c>.
/// </summary>
public static class SchutzkonzeptFelder
{
    public const string DokumentId = "dokumentId";

    public static string JsonName(SchutzkonzeptFeld feld) => feld switch
    {
        SchutzkonzeptFeld.DokumentId => DokumentId,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };
}
