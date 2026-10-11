namespace Zeltlotse.SharedKernel.Ereignisse;

/// <summary>Einheitlicher Umschlag jedes Ereignisses (AD-4).</summary>
/// <param name="EreignisId">Kennung des Ereignisses (UUID v7).</param>
/// <param name="Zeitpunkt">Entstehung in UTC.</param>
/// <param name="OrganisationId">Mandant, falls vorhanden; Quelle des Mandanten-Kontexts beim Empfänger.</param>
/// <param name="DatensatzId">Kennung des auslösenden Aggregats.</param>
/// <param name="Folgenummer">Folgenummer des Aggregats für dieses Ereignis (AD-28).</param>
public sealed record Umschlag(Guid EreignisId, DateTimeOffset Zeitpunkt, Guid? OrganisationId, Guid DatensatzId, long Folgenummer);
