using Zeltlotse.SharedKernel.Datenschutz;

namespace Zeltlotse.SharedKernel.Dateien;

/// <summary>
/// Besonders schützenswerte Dateien (AD-14, AD-15), z. B. Nachweis-Dateien: verschlüsselt mit dem Schlüssel der Person und
/// nur als Chiffre im Dateispeicher (<c>application/octet-stream</c>) unter <c>&lt;modul-präfix&gt;/&lt;kennung&gt;</c>. Eine
/// neue Datei ersetzt die alte unter derselben Kennung. Klartext liegt nie im Speicher.
/// </summary>
public interface IGeschuetzteDateien
{
    Task SpeichernAsync(string modulPraefix, Guid kennung, Guid personId, byte[] inhalt, CancellationToken abbruch = default);

    /// <summary>Der entschlüsselte Inhalt oder <c>null</c>, wenn es die Datei nicht gibt.</summary>
    /// <exception cref="PersonenSchluesselFehltAusnahme">Der Schlüssel der Person ist vernichtet.</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">Chiffre verändert oder einer anderen Person untergeschoben.</exception>
    Task<byte[]?> LesenAsync(string modulPraefix, Guid kennung, CancellationToken abbruch = default);

    /// <summary>Löscht endgültig; eine fehlende Datei ist kein Fehler.</summary>
    Task LoeschenAsync(string modulPraefix, Guid kennung, CancellationToken abbruch = default);
}

internal sealed class GeschuetzteDateien(IDateiSpeicher speicher, IVerschluesselung verschluesselung) : IGeschuetzteDateien
{
    private const string ContentType = "application/octet-stream";

    public async Task SpeichernAsync(string modulPraefix, Guid kennung, Guid personId, byte[] inhalt, CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(inhalt);
        var geschuetzt = await verschluesselung.VerschluesselnAsync(personId, inhalt, abbruch);
        await speicher.SpeichernAsync(modulPraefix, kennung, geschuetzt.AlsBytes(), ContentType, abbruch);
    }

    public async Task<byte[]?> LesenAsync(string modulPraefix, Guid kennung, CancellationToken abbruch = default)
    {
        var datei = await speicher.LesenAsync(modulPraefix, kennung, abbruch);
        return datei is null ? null : await verschluesselung.EntschluesselnBytesAsync(Geschuetzt.AusBytes(datei.Inhalt), abbruch);
    }

    public Task LoeschenAsync(string modulPraefix, Guid kennung, CancellationToken abbruch = default) =>
        speicher.LoeschenAsync(modulPraefix, kennung, abbruch);
}
