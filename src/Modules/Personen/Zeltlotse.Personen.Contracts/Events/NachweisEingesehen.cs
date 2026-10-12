namespace Zeltlotse.Personen.Contracts.Events;

/// <summary>
/// Die gebundene prüfende Person hat einen Nachweis eingesehen und bestätigt (FR-10, AD-9, Tabelle Ereignis-Fluss).
/// Empfänger ist Organisationen (Nachweis-Bestätigung speichern); es berechnet „gültig bis“ aus
/// <paramref name="Ausstellungsdatum"/> und der eigenen Richtlinie und speichert das Ausstellungsdatum nicht. Datensatz im
/// Umschlag ist die Einsicht, die Organisation die der Einsicht.
/// </summary>
public sealed record NachweisEingesehen(
    Guid PersonId,
    Guid NachweisId,
    NachweisArt Art,
    DateOnly Ausstellungsdatum,
    DateTimeOffset EingesehenAm,
    Guid PruefendePersonId);
