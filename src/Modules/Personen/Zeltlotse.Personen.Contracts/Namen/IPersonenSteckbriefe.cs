namespace Zeltlotse.Personen.Contracts.Namen;

/// <summary>
/// Lesende Schnittstelle von Personen (AD-2, AD-8, FR-15): Vorname, Nachname und Herkunft zu einer Menge von Personen, z. B. für
/// das Team auf der öffentlichen Freizeitseite. Was davon öffentlich erscheint, entscheidet der Aufrufer; weitere Profilangaben
/// liefert sie nicht.
/// </summary>
public interface IPersonenSteckbriefe
{
    /// <summary>Je vorhandener Person ihr Steckbrief; unbekannte Kennungen fehlen im Ergebnis.</summary>
    Task<IReadOnlyDictionary<Guid, PersonenSteckbrief>> SteckbriefeAsync(IReadOnlyCollection<Guid> personIds, CancellationToken abbruch = default);
}

/// <summary>Vorname, Nachname und Herkunft (Wohnort) einer Person; jeweils <c>null</c>, solange das Profil sie nicht trägt.</summary>
public sealed record PersonenSteckbrief(string? Vorname, string? Nachname, string? Herkunft);
