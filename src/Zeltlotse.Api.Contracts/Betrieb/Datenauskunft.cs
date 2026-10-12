namespace Zeltlotse.Api.Contracts.Betrieb;

/// <summary>
/// Antwort von <c>GET /api/ich/daten</c> (FR-14, AD-16): alle gespeicherten Daten der eigenen Person, je Modul in Abschnitten.
/// Daraus entstehen im Client auch PDF (Druck) und CSV.
/// </summary>
public sealed record DatenauskunftAntwort(DateTimeOffset ErstelltAm, IReadOnlyList<DatenauskunftAbschnitt> Abschnitte);

/// <summary>Ein Abschnitt der Auskunft, z. B. „Profil“.</summary>
public sealed record DatenauskunftAbschnitt(string Titel, IReadOnlyList<DatenauskunftZeile> Zeilen);

/// <summary>Eine Zeile der Auskunft: Feld und Wert als Text.</summary>
public sealed record DatenauskunftZeile(string Feld, string Wert);
