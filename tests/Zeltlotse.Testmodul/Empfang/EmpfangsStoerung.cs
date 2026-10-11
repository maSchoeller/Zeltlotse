using System.Collections.Concurrent;

namespace Zeltlotse.Testmodul.Empfang;

/// <summary>Lässt einen Empfänger für eine Folgenummer genau einmal scheitern.</summary>
internal sealed class EmpfangsStoerung
{
    private readonly ConcurrentDictionary<(string Empfaenger, long Folgenummer), Func<Exception>> _offen = new();

    public void ScheitertEinmal(string empfaenger, long folgenummer) =>
        _offen[(empfaenger, folgenummer)] = () => new InvalidOperationException("Absichtlicher Fehler im Empfänger.");

    /// <summary>Wie eine abgelaufene HTTP-Anfrage: <see cref="TaskCanceledException"/> ohne angeforderten Abbruch.</summary>
    public void BrichtEinmalAb(string empfaenger, long folgenummer) =>
        _offen[(empfaenger, folgenummer)] = () => new TaskCanceledException("Absichtliche Zeitüberschreitung im Empfänger.");

    public void PruefenUndAusloesen(string empfaenger, long folgenummer)
    {
        if (_offen.TryRemove((empfaenger, folgenummer), out var fehler))
        {
            throw fehler();
        }
    }
}
