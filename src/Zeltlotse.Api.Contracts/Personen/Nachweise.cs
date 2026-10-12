using System.Text.Json.Serialization;

namespace Zeltlotse.Api.Contracts.Personen;

/// <summary>
/// Nachweisart (FR-8) im JSON als camelCase-Wort. Gleiche Namen und Werte wie der Ereignis-Typ von Personen und die Art in
/// <c>Organisationen/</c> (ein Test prüft das).
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
/// Status einer Einsicht in Nachweise. Für die Person selbst erscheint eine abgelaufene Freigabe als <see cref="Angefragt"/>; die
/// Prüfseiten zeigen sie als <see cref="Abgelaufen"/>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EinsichtStatus>))]
public enum EinsichtStatus
{
    [JsonStringEnumMemberName("angefragt")]
    Angefragt,

    [JsonStringEnumMemberName("freigegeben")]
    Freigegeben,

    [JsonStringEnumMemberName("abgeschlossen")]
    Abgeschlossen,

    /// <summary>Nur auf den Prüfseiten: freigegeben, aber die Freigabe ist abgelaufen; die Person muss erneut freigeben.</summary>
    [JsonStringEnumMemberName("abgelaufen")]
    Abgelaufen,
}

/// <summary>Entscheidung der prüfenden Person zu einem Nachweis einer Einsicht.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PostenErgebnis>))]
public enum PostenErgebnis
{
    [JsonStringEnumMemberName("offen")]
    Offen,

    [JsonStringEnumMemberName("bestaetigt")]
    Bestaetigt,

    [JsonStringEnumMemberName("abgelehnt")]
    Abgelehnt,
}

/// <summary>
/// Antwort von <c>GET /api/ich/nachweise</c> (AD-26): nur die eigenen Nachweise und die eigenen offenen Einsichten aller
/// Organisationen.
/// </summary>
public sealed record NachweiseAntwort(IReadOnlyList<NachweisZeile> Nachweise, IReadOnlyList<EinsichtAnfrage> Anfragen);

/// <summary>Ein eigener Nachweis mit seiner Version (AD-19).</summary>
/// <param name="HochgeladenAm">Wann die Datei hochgeladen wurde; <c>null</c>, solange es keine Datei gibt.</param>
/// <param name="Ablehnungshinweis">Der Hinweis der prüfenden Person nach einer Ablehnung, bis Datei oder Datum neu sind.</param>
public sealed record NachweisZeile(Guid Id, uint Version, NachweisArt Art, DateOnly Ausstellungsdatum, DateTimeOffset? HochgeladenAm, string? Ablehnungshinweis);

/// <summary>Eine offene Einsicht: wer angefragt hat (bei automatischer Anfrage <c>null</c>) und wie weit sie ist.</summary>
/// <param name="OrganisationId">Die Organisation, die um Einsicht bittet; den Namen liefert <c>GET /api/ich/organisationen/namen</c>.</param>
/// <param name="FreizeitId">Die Freizeit, aus der die Anfrage kommt; bei einer Anfrage der Organisation <c>null</c>.</param>
public sealed record EinsichtAnfrage(
    Guid Id,
    Guid OrganisationId,
    Guid? FreizeitId,
    PersonenNameAntwort? AngefragtVon,
    EinsichtStatus Status,
    DateTimeOffset AngefragtAm,
    DateTimeOffset? FreigegebenBis);

/// <summary>Vor- und Nachname einer Person; ohne Profil <c>null</c>.</summary>
public sealed record PersonenNameAntwort(string? Vorname, string? Nachname);

/// <summary>Körper von <c>POST /api/ich/nachweise</c>: Art und Ausstellungsdatum sind Pflicht; die Datei folgt getrennt.</summary>
public sealed record NachweisHinzufuegen(NachweisArt? Art, DateOnly? Ausstellungsdatum);

/// <summary>Antwort <c>201</c> von <c>POST /api/ich/nachweise</c>.</summary>
public sealed record NachweisHinzugefuegt(Guid NachweisId);

/// <summary>Antwort <c>200</c> von <c>POST /api/ich/nachweise/{nachweisId}/datei</c> (Multipart-Feld <see cref="NachweisFelder.Datei"/>).</summary>
public sealed record NachweisDateiHochgeladen(DateTimeOffset HochgeladenAm);

/// <summary>Antwort <c>200</c> von <c>POST /api/ich/nachweise/einsichten/{einsichtId}/freigeben</c>.</summary>
public sealed record EinsichtFreigegeben(DateTimeOffset FreigegebenBis);

/// <summary>Bearbeitbare Felder eines Nachweises für <c>PATCH /api/ich/nachweise/{nachweisId}</c> (AD-19).</summary>
public enum NachweisFeld
{
    Ausstellungsdatum,
}

/// <summary>
/// JSON-Namen der Nachweis-Felder: Körper <c>{ "version": n, "alterWert": …, "ausstellungsdatum": "yyyy-MM-dd" }</c>, Antwort
/// <c>{ "version": n, "ausstellungsdatum": … }</c>. <see cref="Datei"/> ist der Name des Multipart-Felds beim Hochladen.
/// </summary>
public static class NachweisFelder
{
    public const string Art = "art";

    public const string Ausstellungsdatum = "ausstellungsdatum";

    public const string Datei = "datei";

    public const string Hinweis = "hinweis";

    /// <summary>Höchstlänge des Ablehnungshinweises (getrimmt).</summary>
    public const int HinweisMaximaleLaenge = 500;

    /// <summary>Format des Ausstellungsdatums im Körper und in der Antwort.</summary>
    public const string Datumsformat = "yyyy-MM-dd";

    public static string JsonName(NachweisFeld feld) => feld switch
    {
        NachweisFeld.Ausstellungsdatum => Ausstellungsdatum,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };

    /// <summary>Das camelCase-Wort einer Art, wie im JSON und im Dateinamen.</summary>
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
/// Eine Zeile der Prüfliste (<c>GET …/personen/nachweise</c>, Leitung oder Org-Admin): eine Person mit Rolle in der Freizeit bzw.
/// Organisation, außer der anfragenden, mit ihrer jüngsten Einsicht in dieser Organisation.
/// </summary>
/// <param name="Anfragbar">Es gibt keine offene Einsicht; „Einsicht anfragen“ legt eine an.</param>
public sealed record PruefPerson(Guid PersonId, string? Vorname, string? Nachname, bool Anfragbar, PruefEinsicht? Einsicht);

/// <summary>Die jüngste Einsicht einer Person in der Prüfliste.</summary>
/// <param name="Status">Mit <see cref="EinsichtStatus.Abgelaufen"/> für eine abgelaufene Freigabe.</param>
/// <param name="FreigegebenBis">Ende der Freigabe, bei <see cref="EinsichtStatus.Abgelaufen"/> der Zeitpunkt des Ablaufs.</param>
/// <param name="Pruefbar">An die anfragende Person gebunden oder von ihr übernehmbar.</param>
public sealed record PruefEinsicht(
    Guid Id,
    EinsichtStatus Status,
    DateTimeOffset AngefragtAm,
    DateTimeOffset? FreigegebenBis,
    DateTimeOffset? AbgeschlossenAm,
    bool Pruefbar);

/// <summary>Körper von <c>POST …/personen/nachweise/anfragen</c>.</summary>
public sealed record EinsichtAnfragen(Guid? PersonId);

/// <summary>Antwort von <c>POST …/personen/nachweise/anfragen</c>: <c>201</c> neu, <c>200</c> schon offen.</summary>
public sealed record EinsichtAngefragt(Guid EinsichtId);

/// <summary>Antwort von <c>GET …/personen/nachweise/einsichten/{einsichtId}</c>: die Einsicht mit ihren Nachweisen.</summary>
/// <param name="FreigegebenBis">Ende der Freigabe, bei <paramref name="FreigabeAbgelaufen"/> der Tag des Ablaufs.</param>
/// <param name="FreigabeAbgelaufen">Freigegeben, aber abgelaufen: Die Person muss erneut freigeben.</param>
public sealed record EinsichtDetail(
    Guid Id,
    Guid PersonId,
    string? Vorname,
    string? Nachname,
    EinsichtStatus Status,
    DateTimeOffset? FreigegebenBis,
    bool FreigabeAbgelaufen,
    IReadOnlyList<EinsichtPosten> Posten);

/// <summary>Ein Nachweis einer freigegebenen Einsicht und die Entscheidung dazu.</summary>
public sealed record EinsichtPosten(
    Guid NachweisId,
    NachweisArt Art,
    DateOnly Ausstellungsdatum,
    DateTimeOffset? HochgeladenAm,
    PostenErgebnis Ergebnis,
    DateTimeOffset? EntschiedenAm);

/// <summary>Körper von <c>POST …/einsichten/{einsichtId}/nachweise/{nachweisId}/ablehnen</c>: Hinweis mit 1–500 Zeichen.</summary>
public sealed record NachweisAblehnen(string? Hinweis);
