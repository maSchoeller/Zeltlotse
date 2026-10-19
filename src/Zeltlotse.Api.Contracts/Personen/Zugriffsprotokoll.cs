using System.Text.Json.Serialization;

namespace Zeltlotse.Api.Contracts.Personen;

/// <summary>Wofür jemand Einsicht hatte (AD-8); im JSON als camelCase-Wort. Wird additiv erweitert (Freizeitpass in V2).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ZugriffZweck>))]
public enum ZugriffZweck
{
    [JsonStringEnumMemberName("nachweiseEingesehen")]
    NachweiseEingesehen = 1,

    /// <summary>Story 2-5: Leitung oder Team hat den Freizeitpass im Freizeit-Kontext gesehen (AD-8).</summary>
    [JsonStringEnumMemberName("freizeitpassEingesehen")]
    FreizeitpassEingesehen = 2,

    /// <summary>Story 4-2: Der Allergen-Abgleich der Küche hat die Allergie-Auswahl gelesen (nur Anzahlen, ohne lesende Person, AD-8).</summary>
    [JsonStringEnumMemberName("allergeneFuerKueche")]
    AllergeneFuerKueche = 3,

    /// <summary>Story 4-4: Der Sonderkost-Abgleich der Küche hat die Ernährungs-Auswahl gelesen (nur Anzahlen, ohne lesende Person, AD-8).</summary>
    [JsonStringEnumMemberName("sonderkostFuerKueche")]
    SonderkostFuerKueche = 4,
}

/// <summary>
/// Ein Eintrag von <c>GET /api/ich/zugriffsprotokoll</c> (AD-8, AD-26, FR-13): wer wann wessen Daten eingesehen hat, für die Person
/// selbst und die Kinder ihrer Familie. <see cref="LesenderName"/> ist <c>null</c>, wenn die lesende Person gelöscht ist. Story 4-2:
/// <see cref="LesendePersonId"/> ist <c>null</c> bei einem Lesen ohne Menschen (Allergen-Abgleich der Küche).
/// </summary>
public sealed record ZugriffsprotokollEintrag(
    DateTimeOffset Am,
    Guid BetroffenePersonId,
    string? BetroffenerName,
    Guid? LesendePersonId,
    string? LesenderName,
    Guid? OrganisationId,
    Guid? FreizeitId,
    ZugriffZweck Zweck);
