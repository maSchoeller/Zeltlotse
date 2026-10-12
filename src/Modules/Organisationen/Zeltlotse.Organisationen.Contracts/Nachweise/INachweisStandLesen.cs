namespace Zeltlotse.Organisationen.Contracts.Nachweise;

/// <summary>
/// Nachweisart, wie sie Organisationen nach außen nennt. Contracts referenzieren keine anderen Contracts; darum gleiche Namen und
/// Werte wie der Ereignis-Typ von Personen (ein Test prüft das).
/// </summary>
public enum NachweisArt
{
    Fuehrungszeugnis = 1,
    Juleica = 2,
    ErsteHilfe = 3,
    Praeventionsschulung = 4,
}

/// <summary>
/// Lesende Schnittstelle von Organisationen (AD-2, FR-11): welche Nachweise die Organisation verlangt und bis wann die Bestätigungen
/// einer Menge von Personen gelten. Läuft unter dem Mandantenfilter; der Mandanten-Kontext ist die Organisation (z. B. die der
/// adressierten Freizeit, AD-6). Liefert keine Namen, keine Dateien und kein Ausstellungsdatum.
/// </summary>
public interface INachweisStandLesen
{
    Task<NachweisStand> StandAsync(IReadOnlyCollection<Guid> personIds, CancellationToken abbruch = default);
}

/// <summary>Pflicht-Arten laut Richtlinie (in Enum-Reihenfolge) und die Bestätigungen der angefragten Personen.</summary>
public sealed record NachweisStand(IReadOnlyList<NachweisArt> PflichtArten, IReadOnlyList<NachweisGueltigkeit> Bestaetigungen);

/// <summary>Eine Bestätigung der Organisation: gilt einschließlich <paramref name="GueltigBis"/>.</summary>
public sealed record NachweisGueltigkeit(Guid PersonId, NachweisArt Art, DateOnly GueltigBis);
