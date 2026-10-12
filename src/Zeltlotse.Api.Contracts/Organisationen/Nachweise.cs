using System.Text.Json.Serialization;

namespace Zeltlotse.Api.Contracts.Organisationen;

/// <summary>
/// Nachweisart (FR-9) im JSON als camelCase-Wort, im Pfad ohne Rücksicht auf Groß- und Kleinschreibung. Gleiche Namen und
/// Werte wie die Art in <c>Personen/</c> (ein Test prüft das).
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

/// <summary>
/// Eine Zeile von <c>GET /api/organisationen/{orgId}/nachweis-richtlinie</c> (Org-Admin, NFR-5): ob die Art Pflicht ist und
/// wie viele Monate ab Ausstellung eine Bestätigung gilt.
/// </summary>
public sealed record RichtlinieZeile(NachweisArt Art, uint Version, bool Pflicht, int GueltigkeitMonate);

/// <summary>Bearbeitbare Felder der Nachweis-Richtlinie für <c>PATCH /api/organisationen/{orgId}/nachweis-richtlinie/{art}</c> (AD-19).</summary>
public enum RichtlinieFeld
{
    Pflicht,
    GueltigkeitMonate,
}

/// <summary>
/// JSON-Namen der Richtlinien-Felder. Werte stehen als Text: <c>pflicht</c> als <c>"true"</c> oder <c>"false"</c>,
/// <c>gueltigkeitMonate</c> als Zahl zwischen <see cref="GueltigkeitMindestens"/> und <see cref="GueltigkeitHoechstens"/>.
/// </summary>
public static class RichtlinieFelder
{
    public const string Pflicht = "pflicht";

    public const string GueltigkeitMonate = "gueltigkeitMonate";

    public const int GueltigkeitMindestens = 1;

    public const int GueltigkeitHoechstens = 120;

    public static string JsonName(RichtlinieFeld feld) => feld switch
    {
        RichtlinieFeld.Pflicht => Pflicht,
        RichtlinieFeld.GueltigkeitMonate => GueltigkeitMonate,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };

    /// <summary>Das camelCase-Wort einer Art, wie im JSON und im Pfad.</summary>
    public static string JsonName(NachweisArt art) => art switch
    {
        NachweisArt.Fuehrungszeugnis => "fuehrungszeugnis",
        NachweisArt.Juleica => "juleica",
        NachweisArt.ErsteHilfe => "ersteHilfe",
        NachweisArt.Praeventionsschulung => "praeventionsschulung",
        _ => throw new ArgumentOutOfRangeException(nameof(art), art, null),
    };
}

/// <summary>
/// Eine Zeile von <c>GET /api/ich/organisationen/nachweis-bestaetigungen</c> (AD-26): eine eigene Bestätigung einer
/// Organisation. Die Organisation speichert nur Art, eingesehen am, gültig bis und von wem (AD-9).
/// </summary>
/// <param name="Abgelaufen">„Gültig bis“ liegt vor heute (Kalendertag Europe/Berlin); rechnet der Server.</param>
public sealed record NachweisBestaetigungZeile(
    Guid OrganisationId,
    string Organisation,
    NachweisArt Art,
    DateTimeOffset EingesehenAm,
    DateOnly GueltigBis,
    bool Abgelaufen,
    GeprueftVon GeprueftVon);

/// <summary>
/// Eine Zeile von <c>GET /api/ich/organisationen/namen?id=…</c> (AD-26): nur der Name einer Organisation, damit die Person sieht,
/// wer um Einsicht bittet. Höchstens <see cref="OrganisationsNamen.HoechstensKennungen"/> Kennungen je Abfrage.
/// </summary>
public sealed record OrganisationName(Guid OrganisationId, string Name);

/// <summary>Grenzen von <c>GET /api/ich/organisationen/namen</c>.</summary>
public static class OrganisationsNamen
{
    /// <summary>Name des Abfrageparameters, der sich wiederholen darf.</summary>
    public const string Parameter = "id";

    public const int HoechstensKennungen = 20;
}

/// <summary>Name der prüfenden Person; ohne Profil <c>null</c>.</summary>
public sealed record GeprueftVon(string? Vorname, string? Nachname);
