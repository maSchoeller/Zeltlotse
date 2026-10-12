using System.Text.Json.Serialization;

namespace Zeltlotse.Api.Contracts.Freizeiten;

/// <summary>
/// Nachweisart in den Warnhinweisen der Freizeit (FR-11), im JSON als camelCase-Wort. Gleiche Namen und Werte wie die Art in
/// <c>Personen/</c> und <c>Organisationen/</c> (ein Test prüft das).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<NachweisArt>))]
public enum NachweisArt
{
    [JsonStringEnumMemberName("fuehrungszeugnis")]
    Fuehrungszeugnis = 1,

    [JsonStringEnumMemberName("juleica")]
    Juleica = 2,

    [JsonStringEnumMemberName("ersteHilfe")]
    ErsteHilfe = 3,

    [JsonStringEnumMemberName("praeventionsschulung")]
    Praeventionsschulung = 4,
}

/// <summary>Warum ein Pflicht-Nachweis eine Warnung auslöst.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<NachweisWarnungGrund>))]
public enum NachweisWarnungGrund
{
    /// <summary>Die Organisation hat keine Bestätigung.</summary>
    [JsonStringEnumMemberName("fehlt")]
    Fehlt,

    /// <summary>Die Bestätigung gilt heute noch, aber nicht bis zum letzten Freizeittag.</summary>
    [JsonStringEnumMemberName("laeuftAb")]
    LaeuftAb,

    /// <summary>Die Bestätigung ist schon abgelaufen (vor heute, Europe/Berlin) und galt nicht bis zum letzten Freizeittag.</summary>
    [JsonStringEnumMemberName("abgelaufen")]
    Abgelaufen,
}

/// <summary>
/// Antwort von <c>GET /api/freizeiten/{freizeitId}/nachweis-warnungen</c> (Leitung, FR-11): nur Personen des Teams mit mindestens
/// einer Warnung, ohne die anfragende Person, nach Nachname und Vorname.
/// </summary>
public sealed record NachweisWarnungen(IReadOnlyList<NachweisWarnungPerson> Personen);

/// <summary>Eine Person mit ihren Warnungen in der Reihenfolge der Nachweisarten; Namen ohne Profil sind <c>null</c>.</summary>
public sealed record NachweisWarnungPerson(Guid PersonId, string? Vorname, string? Nachname, IReadOnlyList<NachweisWarnung> Warnungen);

/// <summary>Eine Warnung zu einem Pflicht-Nachweis.</summary>
/// <param name="GueltigBis">Letzter gültiger Tag der Bestätigung; fehlt bei <see cref="NachweisWarnungGrund.Fehlt"/>.</param>
public sealed record NachweisWarnung(
    NachweisArt Art,
    NachweisWarnungGrund Grund,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? GueltigBis);
