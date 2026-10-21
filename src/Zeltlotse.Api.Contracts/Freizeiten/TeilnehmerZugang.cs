namespace Zeltlotse.Api.Contracts.Freizeiten;

/// <summary>
/// Story 6-4 (CAP-38, FR-64, AD-11): Antwort von <c>GET /api/freizeiten/{freizeitId}/qr-zugang</c> und <c>POST …/qr-zugang/erneuern</c> (Leitung, nur mit
/// dem Modul Teilnehmer-Zugang): die Adresse des dauerhaft geteilten QR-Links und die Matrix des QR-Codes. Gibt es noch keinen Link, sind beide Felder
/// <c>null</c>.
/// </summary>
/// <param name="Adresse"><c>{öffentliche Adresse}/app/teilnehmer/{token}</c>.</param>
/// <param name="Zeilen">
/// Die Matrix des QR-Codes (Fehlerkorrektur M, mit Ruhezone) als gleich lange Zeilen aus <c>'1'</c> (dunkel) und <c>'0'</c> (hell). Erzeugt auf dem
/// Server, der Client zeichnet sie nur.
/// </param>
public sealed record QrZugangAnsicht(string? Adresse, IReadOnlyList<string>? Zeilen)
{
    /// <summary>Nie die Adresse (mit dem Token) ausgeben (AD-22).</summary>
    public override string ToString() => nameof(QrZugangAnsicht);
}

/// <summary>Story 6-4: Körper von <c>POST /api/teilnehmer/einloesen</c>: das Token aus dem QR-Link; erst dieser POST tauscht es gegen das Gast-Cookie.</summary>
public sealed record TeilnehmerEinloesen(string? Token)
{
    /// <summary>Nie das Token ausgeben (AD-22).</summary>
    public override string ToString() => nameof(TeilnehmerEinloesen);
}

/// <summary>Story 6-4: Antwort von <c>GET /api/teilnehmer</c> (Gast-Sitzung): nur Name und Zeitraum der Freizeit.</summary>
public sealed record TeilnehmerFreizeit(string Name, DateOnly ErsterTag, DateOnly LetzterTag);

/// <summary>
/// Story 6-4: Antwort von <c>GET /api/teilnehmer/infos</c> (Gast-Sitzung): die Dokumente der Freizeit mit Sichtbarkeit „Eltern und Teilnehmende“, nach
/// Titel. Die Datei liefert <c>GET /api/teilnehmer/infos/{dokumentId}</c>.
/// </summary>
public sealed record TeilnehmerInfos(IReadOnlyList<DokumentVerweis> Dokumente);
