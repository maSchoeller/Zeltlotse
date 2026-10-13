using System.Text.Json.Serialization;
using Zeltlotse.Api.Contracts.Gemeinsam;

namespace Zeltlotse.Api.Contracts.Freizeiten;

/// <summary>Art eines Anmeldelinks (AD-11, FR-16); im JSON als Name, in Routen als <see cref="AnmeldelinkArten"/>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AnmeldelinkArt>))]
public enum AnmeldelinkArt
{
    Teilnehmende,
    Mitarbeitende,
}

/// <summary>Routenwerte der Anmeldelink-Arten, z. B. <c>POST /api/freizeiten/{freizeitId}/anmeldelinks/teilnehmende/erneuern</c>.</summary>
public static class AnmeldelinkArten
{
    public const string Teilnehmende = "teilnehmende";

    public const string Mitarbeitende = "mitarbeitende";

    public static string Routenwert(AnmeldelinkArt art) => art switch
    {
        AnmeldelinkArt.Teilnehmende => Teilnehmende,
        AnmeldelinkArt.Mitarbeitende => Mitarbeitende,
        _ => throw new ArgumentOutOfRangeException(nameof(art), art, null),
    };

    /// <summary>Die Art zum Routenwert; <c>null</c> für einen unbekannten Wert.</summary>
    public static AnmeldelinkArt? Aus(string? routenwert) => routenwert switch
    {
        Teilnehmende => AnmeldelinkArt.Teilnehmende,
        Mitarbeitende => AnmeldelinkArt.Mitarbeitende,
        _ => null,
    };
}

/// <summary>
/// Antwort von <c>GET /api/freizeiten/{freizeitId}/freizeitseite</c> (Leitung): die Angaben der öffentlichen Freizeitseite mit
/// Version (AD-19) und die beiden Anmeldelinks.
/// </summary>
/// <param name="Anmeldelinks">Teilnehmende vor Mitarbeitende; fehlt ein Link, steht er nicht in der Liste.</param>
/// <param name="Teilnahmebedingungen">Text der Teilnahmebedingungen, der im Anmeldeformular steht (Story 2-2).</param>
/// <param name="KontaktMail">Mail-Adresse der Leitung für Absagen (Story 2-3, FR-21).</param>
/// <param name="KontaktTelefon">Telefon der Leitung für Absagen (Story 2-3, FR-21).</param>
public sealed record FreizeitseiteEinstellungen(
    uint Version,
    string? Beschreibung,
    string? Ort,
    int? HoechstzahlTeilnehmende,
    IReadOnlyList<Anmeldelink> Anmeldelinks,
    string? Teilnahmebedingungen = null,
    string? KontaktMail = null,
    string? KontaktTelefon = null);

/// <summary>Ein Anmeldelink mit seiner vollständigen Adresse (öffentliche Adresse plus <c>/freizeit/{token}</c>).</summary>
public sealed record Anmeldelink(AnmeldelinkArt Art, string Adresse);

/// <summary>Bearbeitbare Felder der Freizeitseite für <c>PATCH /api/freizeiten/{freizeitId}/freizeitseite</c> (AD-19).</summary>
public enum FreizeitseiteFeld
{
    Beschreibung,
    Ort,
    HoechstzahlTeilnehmende,
    Teilnahmebedingungen,
    KontaktMail,
    KontaktTelefon,
}

/// <summary>
/// JSON-Namen und Grenzen der Felder für <c>PATCH /api/freizeiten/{freizeitId}/freizeitseite</c>: Körper
/// <c>{ "version": n, "alterWert": …, "&lt;feld&gt;": neu }</c> mit Text (die Höchstzahl als Text oder Zahl, leer = keine), Antwort
/// <c>{ "version": n, "&lt;feld&gt;": gespeichert }</c> als Text.
/// </summary>
public static class FreizeitseiteFelder
{
    public const string Beschreibung = "beschreibung";

    public const string Ort = "ort";

    public const string HoechstzahlTeilnehmende = "hoechstzahlTeilnehmende";

    public const string Teilnahmebedingungen = AnmeldungFelder.Teilnahmebedingungen;

    public const string KontaktMail = "kontaktMail";

    public const string KontaktTelefon = "kontaktTelefon";

    /// <summary>Höchstlänge der Mail-Adresse für Absagen (wie jede Mail-Adresse); sie muss ein „@“ enthalten.</summary>
    public const int KontaktMailMaximaleLaenge = 254;

    public const int KontaktTelefonMaximaleLaenge = 40;

    public const int BeschreibungMaximaleLaenge = 2000;

    public const int OrtMaximaleLaenge = 100;

    public const int TeilnahmebedingungenMaximaleLaenge = AnmeldungFelder.TeilnahmebedingungenMaximaleLaenge;

    public const int HoechstzahlMindestens = 1;

    public const int HoechstzahlHoechstens = 1000;

    public static string JsonName(FreizeitseiteFeld feld) => feld switch
    {
        FreizeitseiteFeld.Beschreibung => Beschreibung,
        FreizeitseiteFeld.Ort => Ort,
        FreizeitseiteFeld.HoechstzahlTeilnehmende => HoechstzahlTeilnehmende,
        FreizeitseiteFeld.Teilnahmebedingungen => Teilnahmebedingungen,
        FreizeitseiteFeld.KontaktMail => KontaktMail,
        FreizeitseiteFeld.KontaktTelefon => KontaktTelefon,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };
}

/// <summary>
/// Antwort von <c>GET /api/freizeiten/oeffentlich/{token}</c> (anonym, AD-11): die öffentliche Freizeitseite (FR-15, FR-67).
/// </summary>
/// <param name="Organisation">Name der Organisation.</param>
/// <param name="Art">Art des aufgerufenen Anmeldelinks.</param>
/// <param name="Ausgebucht">Nur beim Link für Teilnehmende: bestätigte Teilnehmende erreichen die Höchstzahl.</param>
/// <param name="Team">Leitung vor Team; nur Vorname, Initialen, Rolle und Herkunft.</param>
/// <param name="Teilnahmebedingungen">Text der Teilnahmebedingungen für das Anmeldeformular; leer, wenn keine hinterlegt sind.</param>
/// <param name="Dokumente">Story 2-7: die Dokumente der Freizeit mit Sichtbarkeit „öffentlich“, nach Titel (FR-43).</param>
public sealed record Freizeitseite(
    string Organisation,
    string Name,
    DateOnly ErsterTag,
    DateOnly LetzterTag,
    string? Ort,
    string? Beschreibung,
    AnmeldelinkArt Art,
    bool Ausgebucht,
    IReadOnlyList<TeamPerson> Team,
    string? Teilnahmebedingungen = null,
    IReadOnlyList<DokumentVerweis>? Dokumente = null);

/// <summary>Eine Person des Teams auf der Freizeitseite: kein Nachname, keine Mail-Adresse, keine Kennung.</summary>
public sealed record TeamPerson(string Vorname, string Initialen, FreizeitRolle Rolle, string? Herkunft);

/// <summary>
/// Antwort von <c>GET /api/freizeiten/oeffentlich/{token}/vertrauensseite</c> (anonym, FR-66): die gespeicherten Einstellungen, aus
/// denen die Vertrauensseite entsteht.
/// </summary>
/// <param name="FreigabeIntervallTage">Beim Anlegen der Freizeit festgeschriebenes Intervall des Freigabefensters.</param>
/// <param name="FreigabeBis">Letzter Tag des Freigabefensters.</param>
/// <param name="FreizeitDatenMonate">Löschfrist der Organisation für Daten einer Freizeit, ab Freizeitende.</param>
/// <param name="NachweisBestaetigungMonate">Löschfrist der Organisation für abgelaufene Nachweis-Bestätigungen.</param>
/// <param name="PflichtNachweise">Pflicht-Nachweise der Organisation als deutsche Bezeichnungen.</param>
/// <param name="FragenTeilnehmende">Texte der Fragen, die der Freizeitpass für Teilnehmende abfragt (Story 2-4, FR-22); leer = keine.</param>
/// <param name="FragenMitarbeitende">Texte der Fragen, die der Freizeitpass für Mitarbeitende abfragt (Story 2-4); leer = keine.</param>
public sealed record Vertrauensseite(
    string Organisation,
    string Name,
    DateOnly ErsterTag,
    DateOnly LetzterTag,
    int FreigabeIntervallTage,
    DateOnly FreigabeBis,
    int FreizeitDatenMonate,
    int NachweisBestaetigungMonate,
    IReadOnlyList<string> PflichtNachweise,
    IReadOnlyList<string>? FragenTeilnehmende = null,
    IReadOnlyList<string>? FragenMitarbeitende = null);
