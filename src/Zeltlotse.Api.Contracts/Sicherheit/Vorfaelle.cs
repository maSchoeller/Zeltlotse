using System.Text.Json.Serialization;

namespace Zeltlotse.Api.Contracts.Sicherheit;

/// <summary>Story 2-10: Art eines Eintrags im Vorfall-Protokoll (CAP-17, FR-31); im JSON als Name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<VorfallArt>))]
public enum VorfallArt
{
    /// <summary>Was passiert ist.</summary>
    Geschehen = 1,

    /// <summary>Welches Medikament gegeben wurde und wie viel.</summary>
    Medikament = 2,

    /// <summary>Was aus dem Erste-Hilfe-Kasten entnommen wurde.</summary>
    Entnahme = 3,
}

/// <summary>
/// Story 2-10: Körper von <c>POST /api/freizeiten/{freizeitId}/sicherheit/vorfaelle</c> (Leitung und Team): ein neuer, unveränderlicher
/// Eintrag zu einer Person der Freizeit. Die Kennung vergibt das Gerät (AD-18); doppeltes Senden legt keinen zweiten Eintrag an.
/// </summary>
/// <param name="Id">Kennung des Eintrags (UUID v7 vom Gerät).</param>
/// <param name="PersonId">Die betroffene Person; mit ihrem Schlüssel wird der Text verschlüsselt (AD-14).</param>
/// <param name="Text">Höchstens <see cref="VorfallFelder.TextMaximaleLaenge"/> Zeichen; wird getrimmt.</param>
/// <param name="ErfasstAm">
/// Story 6-5: Wann das Gerät den Eintrag geschrieben hat (offline geschriebene Einträge kommen später an). Der Server übernimmt
/// <c>min(erfasstAm, jetzt)</c>; ohne Angabe gilt die Serverzeit.
/// </param>
public sealed record VorfallEintragNeu(
    Guid Id,
    Guid PersonId,
    VorfallArt Art,
    string? Text,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? ErfasstAm = null);

/// <summary>
/// Story 2-10: Ein Eintrag im Vorfall-Protokoll, wie Leitung, Team und (im Archiv) Org-Admins ihn lesen. Bei gelöschten Personen ist der
/// Vorname „Gelöschte Person“ und der Nachname <c>null</c>.
/// </summary>
/// <param name="Text">Der Klartext; <c>null</c>, wenn der Schlüssel der betroffenen Person vernichtet ist.</param>
/// <param name="ErfasstAm">Wann der Eintrag gespeichert wurde.</param>
public sealed record VorfallEintrag(
    Guid Id,
    Guid PersonId,
    string? Vorname,
    string? Nachname,
    VorfallArt Art,
    string? Text,
    DateTimeOffset ErfasstAm,
    string? VerfasserVorname,
    string? VerfasserNachname);

/// <summary>Story 2-10: Die archivierten Einträge einer abgeschlossenen Freizeit im Archiv der Organisation.</summary>
/// <param name="Name">Name der Freizeit.</param>
/// <param name="Eintraege">Neueste zuerst.</param>
public sealed record VorfallArchivFreizeit(Guid FreizeitId, string Name, IReadOnlyList<VorfallEintrag> Eintraege);

/// <summary>JSON-Namen der Felder von <see cref="VorfallEintragNeu"/> für Feldfehler (<c>400</c>) und die Höchstlänge des Texts.</summary>
public static class VorfallFelder
{
    public const string Id = "id";

    public const string PersonId = "personId";

    public const string Art = "art";

    public const string Text = "text";

    /// <summary>Story 6-5: Gerätezeit des Eintrags.</summary>
    public const string ErfasstAm = "erfasstAm";

    public const int TextMaximaleLaenge = 2000;
}
