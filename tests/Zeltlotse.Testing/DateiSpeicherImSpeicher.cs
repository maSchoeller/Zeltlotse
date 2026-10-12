using System.Collections.Concurrent;
using Zeltlotse.SharedKernel.Dateien;

namespace Zeltlotse.Testing;

/// <summary>
/// <see cref="IDateiSpeicher"/> im Arbeitsspeicher für Fachlogik-Tests ohne S3-Container. Die HTTP-Tests laufen gegen den
/// echten S3-Adapter (<see cref="S3Container"/>).
/// </summary>
public sealed class DateiSpeicherImSpeicher : IDateiSpeicher
{
    private readonly ConcurrentDictionary<string, GespeicherteDatei> _dateien = new(StringComparer.Ordinal);

    /// <summary>Die rohen gespeicherten Objekte je Schlüssel <c>&lt;präfix&gt;/&lt;kennung&gt;</c>.</summary>
    public IReadOnlyDictionary<string, GespeicherteDatei> Objekte => _dateien;

    public Task SpeichernAsync(string modulPraefix, Guid kennung, byte[] inhalt, string contentType, CancellationToken abbruch = default)
    {
        _dateien[DateiSchluessel.Bilden(modulPraefix, kennung)] = new GespeicherteDatei([.. inhalt], contentType);
        return Task.CompletedTask;
    }

    public Task<GespeicherteDatei?> LesenAsync(string modulPraefix, Guid kennung, CancellationToken abbruch = default) =>
        Task.FromResult(_dateien.TryGetValue(DateiSchluessel.Bilden(modulPraefix, kennung), out var datei)
            ? new GespeicherteDatei([.. datei.Inhalt], datei.ContentType)
            : null);

    public Task LoeschenAsync(string modulPraefix, Guid kennung, CancellationToken abbruch = default)
    {
        _dateien.TryRemove(DateiSchluessel.Bilden(modulPraefix, kennung), out _);
        return Task.CompletedTask;
    }

    /// <summary>Ersetzt die rohen Bytes eines Objekts (nur für Manipulationstests).</summary>
    public void RohSetzen(string modulPraefix, Guid kennung, byte[] inhalt) =>
        _dateien[DateiSchluessel.Bilden(modulPraefix, kennung)] = new GespeicherteDatei(inhalt, "application/octet-stream");
}
