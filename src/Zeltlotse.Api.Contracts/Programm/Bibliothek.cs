namespace Zeltlotse.Api.Contracts.Programm;

/// <summary>
/// Story 3-6: Ein Eintrag der Programmbibliothek der Organisation (CAP-24, FR-41) in <c>GET /api/freizeiten/{freizeitId}/programm/bibliothek</c>
/// und <c>GET /api/organisationen/{orgId}/programmbibliothek</c>: eine Momentaufnahme eines abgelegten Programmpunkts ohne Personenbezug.
/// Einträge lassen sich nicht bearbeiten; der Org-Admin entfernt sie.
/// </summary>
/// <param name="AbgelegtAm">Zeitpunkt des Ablegens; unterscheidet mehrere Fassungen desselben Titels.</param>
/// <param name="Material">Das Material des Programmpunkts beim Ablegen, nach Bezeichnung.</param>
/// <param name="Aufgaben">Die Aufgaben am Programmpunkt beim Ablegen, nach Phase und Titel.</param>
public sealed record BibliotheksEintrag(
    Guid Id,
    string Titel,
    DateTimeOffset AbgelegtAm,
    IReadOnlyList<BibliotheksMaterial> Material,
    IReadOnlyList<BibliotheksAufgabe> Aufgaben);

/// <summary>Story 3-6: Ein Material eines Bibliothekseintrags: nur Bezeichnung und Menge, ohne Status, Mitbringen oder Reservierung.</summary>
public sealed record BibliotheksMaterial(string Bezeichnung, string? Menge);

/// <summary>Story 3-6: Eine Aufgabe eines Bibliothekseintrags: nur Titel und Phase, ohne Übernahme und Erledigt.</summary>
public sealed record BibliotheksAufgabe(string Titel, AufgabenPhase Phase);

/// <summary>
/// Story 3-6: Körper von <c>POST /api/freizeiten/{freizeitId}/programm/bibliothek/{eintragId}/uebernehmen</c>: der Tag und die Uhrzeit des neuen
/// Programmpunkts.
/// </summary>
/// <param name="Tag">Ein Tag der Freizeit (Pflicht).</param>
/// <param name="Beginn">Uhrzeit als <c>HH:mm</c>; leer heißt ohne Uhrzeit.</param>
public sealed record BibliotheksUebernahme(DateOnly? Tag, string? Beginn);
