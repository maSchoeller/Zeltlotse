namespace Zeltlotse.SharedKernel.Sicherheitsprotokoll;

/// <summary>Sicherheitsrelevante Ereignisse (AD-22). Werte sind gespeichert; nur anhängen, nie umnummerieren.</summary>
public enum SicherheitsEreignisArt : short
{
    Anmeldung = 1,
    AnmeldungFehlgeschlagen = 2,
    LinkEinloesungFehlgeschlagen = 3,
    Rollenvergabe = 4,
    Export = 5,
    Loeschung = 6,
}

/// <summary>
/// Schreibt in das Sicherheitsprotokoll im Schema <c>betrieb</c> des API-Hosts (AD-22): nur Kennungen, kein Freitext, keine IP.
/// Jede Zeile wird in eigener Transaktion gespeichert, unabhängig von der Transaktion des Aufrufers.
/// </summary>
public interface ISicherheitsprotokoll
{
    Task SchreibenAsync(
        SicherheitsEreignisArt art,
        Guid? personId = null,
        Guid? organisationId = null,
        Guid? bezugId = null,
        CancellationToken abbruch = default);
}

/// <summary>Feste Aufbewahrungsfrist des Sicherheitsprotokolls (bis zur Prüfung OP-6).</summary>
public static class SicherheitsprotokollFrist
{
    public static readonly TimeSpan Dauer = TimeSpan.FromDays(365);
}
