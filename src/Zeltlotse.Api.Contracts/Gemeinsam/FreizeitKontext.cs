using System.Text.Json.Serialization;

namespace Zeltlotse.Api.Contracts.Gemeinsam;

/// <summary>
/// Antwort von <c>GET /api/freizeiten/{freizeitId}/kontext</c> (AD-29, Besitzer: Freizeiten): Phase, Rollen und
/// Funktions-Schalter der Freizeit für die anfragende Person sowie die erlaubten Aktionen. Die Oberfläche entscheidet
/// ausschließlich über <see cref="Erlaubt"/>, nie über die Rollen.
/// </summary>
/// <param name="FreizeitId">Kennung der Freizeit.</param>
/// <param name="Name">Name der Freizeit, z. B. für die App-Leiste.</param>
/// <param name="Phase">Phase nach AD-17.</param>
/// <param name="Rollen">Rollen der anfragenden Person in dieser Freizeit.</param>
/// <param name="Schalter">Eingeschaltete Funktions-Schalter (AD-20).</param>
/// <param name="Erlaubt">Erlaubte <see cref="Aktionsschluessel"/>, berechnet aus denselben Richtlinien wie die Endpunkte.</param>
public sealed record FreizeitKontext(
    Guid FreizeitId,
    string Name,
    Phase Phase,
    IReadOnlyList<FreizeitRolle> Rollen,
    IReadOnlyList<FunktionsSchalter> Schalter,
    IReadOnlyList<string> Erlaubt);

/// <summary>Phase einer Freizeit (AD-17); im JSON als Name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Phase>))]
public enum Phase
{
    InVorbereitung,
    Laeuft,
    Nachbereitung,
    Gelaufen,
}

/// <summary>Rolle einer Person in einer Freizeit; im JSON als Name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FreizeitRolle>))]
public enum FreizeitRolle
{
    Leitung,
    Team,
}

/// <summary>Funktions-Schalter einer Freizeit (AD-20); im JSON als Name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FunktionsSchalter>))]
public enum FunktionsSchalter
{
    Verpflegung,
    Finanzen,
    TeilnehmerZugang,
}

/// <summary>
/// Schlüssel der erlaubten Aktionen (AD-29) in <see cref="FreizeitKontext.Erlaubt"/> und den <c>erlaubt</c>-Listen der
/// Antworten. Jede Fach-Story trägt ihre Schlüssel hier ein. Ändert sich nur mit einer Änderung der Architektur-Spine (AD-5).
/// </summary>
public static class Aktionsschluessel
{
    /// <summary>Organisationen anlegen und den ersten Org-Admin einladen (Global Admin, in <c>GET /api/ich/konto</c>).</summary>
    public const string OrganisationenAnlegen = "organisationen-anlegen";

    /// <summary>Rollen der Organisation sehen und Personen einladen (Org-Admin, in <c>GET /api/organisationen/{orgId}</c>).</summary>
    public const string OrgRollenVergeben = "org-rollen-vergeben";

    /// <summary>Freizeiten anlegen (Org-Admin, in <c>GET /api/organisationen/{orgId}/freizeiten</c>).</summary>
    public const string FreizeitenAnlegen = "freizeiten-anlegen";

    /// <summary>Freizeit verwalten und die Leitung einladen (Org-Admin, in <c>GET /api/organisationen/{orgId}/freizeiten</c>).</summary>
    public const string LeitungEinsetzen = "leitung-einsetzen";

    /// <summary>
    /// Nachweise prüfen: Einsicht anfragen, übernehmen, bestätigen oder ablehnen (Leitung im Freizeit-Kontext, Org-Admin in
    /// <c>GET /api/organisationen/{orgId}</c>).
    /// </summary>
    public const string NachweisePruefen = "nachweise-pruefen";

    /// <summary>Die Nachweis-Richtlinie der Organisation bearbeiten (Org-Admin, in <c>GET /api/organisationen/{orgId}</c>).</summary>
    public const string NachweisRichtlinieVerwalten = "nachweis-richtlinie-verwalten";

    /// <summary>Die Löschfristen der Organisation bearbeiten (Org-Admin, in <c>GET /api/organisationen/{orgId}</c>, NFR-5).</summary>
    public const string LoeschfristenVerwalten = "loeschfristen-verwalten";

    /// <summary>
    /// Einstellungen der Freizeit: Freizeitseite bearbeiten und Anmeldelinks sehen und erneuern (Leitung im Freizeit-Kontext,
    /// FR-15, FR-16).
    /// </summary>
    public const string FreizeitEinstellungen = "freizeit-einstellungen";
}
