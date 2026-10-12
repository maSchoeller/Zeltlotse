using System.Text.Json.Serialization;

namespace Zeltlotse.Api.Contracts.Personen;

/// <summary>Wofür jemand Einsicht hatte (AD-8); im JSON als camelCase-Wort. Wird additiv erweitert (Freizeitpass in V2).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ZugriffZweck>))]
public enum ZugriffZweck
{
    [JsonStringEnumMemberName("nachweiseEingesehen")]
    NachweiseEingesehen = 1,
}

/// <summary>
/// Ein Eintrag von <c>GET /api/ich/zugriffsprotokoll</c> (AD-8, AD-26, FR-13): wer wann wessen Daten eingesehen hat, für die Person
/// selbst und die Kinder ihrer Familie. <see cref="LesenderName"/> ist <c>null</c>, wenn die lesende Person gelöscht ist.
/// </summary>
public sealed record ZugriffsprotokollEintrag(
    DateTimeOffset Am,
    Guid BetroffenePersonId,
    string? BetroffenerName,
    Guid LesendePersonId,
    string? LesenderName,
    Guid? OrganisationId,
    Guid? FreizeitId,
    ZugriffZweck Zweck);
