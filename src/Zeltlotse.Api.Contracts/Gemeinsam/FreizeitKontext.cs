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
/// Schlüssel der Aktionen in <see cref="FreizeitKontext.Erlaubt"/> (AD-29). Jede Fach-Story trägt ihre Schlüssel hier ein;
/// noch gibt es keine. Ändert sich nur mit einer Änderung der Architektur-Spine (AD-5).
/// </summary>
public static class Aktionsschluessel;
