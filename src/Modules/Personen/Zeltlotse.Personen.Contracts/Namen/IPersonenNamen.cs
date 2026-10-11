namespace Zeltlotse.Personen.Contracts.Namen;

/// <summary>
/// Lesende Schnittstelle von Personen (AD-2, AD-8): Vor- und Nachname zu einer Menge von Personen, z. B. für die Rollenliste
/// einer Organisation. Liefert nur Namen, keine weiteren Profilangaben.
/// </summary>
public interface IPersonenNamen
{
    /// <summary>Je vorhandener Person ihr Name; unbekannte Kennungen fehlen im Ergebnis.</summary>
    Task<IReadOnlyDictionary<Guid, PersonenName>> NamenAsync(IReadOnlyCollection<Guid> personIds, CancellationToken abbruch = default);
}

/// <summary>Vor- und Nachname einer Person; beide fehlen, solange das Profil sie nicht trägt.</summary>
public sealed record PersonenName(string? Vorname, string? Nachname);
