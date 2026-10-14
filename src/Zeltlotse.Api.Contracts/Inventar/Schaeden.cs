using System.Text.Json.Serialization;

namespace Zeltlotse.Api.Contracts.Inventar;

/// <summary>Story 3-4: Art eines Eintrags auf der Schadens- und Verlustliste (FR-32, FR-38); im JSON als Name. Werte sind gespeichert; nur anhängen.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SchadenArt>))]
public enum SchadenArt
{
    /// <summary>Etwas ist kaputtgegangen.</summary>
    Schaden = 1,

    /// <summary>Etwas ist verloren gegangen.</summary>
    Verlust = 2,

    /// <summary>Etwas wurde aus dem Erste-Hilfe-Kasten entnommen (von Hand oder aus dem Vorfall-Protokoll).</summary>
    Entnahme = 3,
}

/// <summary>
/// Story 3-4: Ein Eintrag auf der Schadens- und Verlustliste einer Freizeit. Nach dem Abschluss der Freizeit ist er ein Anschaffungswunsch an die
/// Organisation.
/// </summary>
/// <param name="Version">Versionsnummer für den Autosave der Bezeichnung (AD-19).</param>
/// <param name="ErfasstAm">Wann der Eintrag entstand.</param>
/// <param name="VorfallEintragId">
/// Bei einer Entnahme aus dem Vorfall-Protokoll die Kennung des Vorfall-Eintrags, sonst <c>null</c>. Den Text liest die Freizeit-Seite aus dem
/// Vorfall-Protokoll dazu; Inventar speichert ihn nicht.
/// </param>
public sealed record SchadenEintrag(Guid Id, uint Version, SchadenArt Art, string Bezeichnung, DateTimeOffset ErfasstAm, Guid? VorfallEintragId);

/// <summary>Story 3-4: Körper von <c>POST /api/freizeiten/{freizeitId}/inventar/schaeden</c>.</summary>
/// <param name="Art">Pflicht.</param>
/// <param name="Bezeichnung">1 bis <see cref="SchadenFelder.BezeichnungMaximaleLaenge"/> Zeichen; wird getrimmt.</param>
public sealed record SchadenNeu(SchadenArt? Art, string? Bezeichnung);

/// <summary>Story 3-4: Bearbeitbare Felder eines Eintrags für <c>PATCH /api/freizeiten/{freizeitId}/inventar/schaeden/{eintragId}</c> (AD-19).</summary>
public enum SchadenFeld
{
    Bezeichnung,
}

/// <summary>
/// Story 3-4: Anschaffungswünsche einer abgeschlossenen Freizeit, Antwort von <c>GET /api/organisationen/{orgId}/inventar/anschaffungswuensche</c>
/// (Org-Admin und Org-Überblick): die Einträge der Schadens- und Verlustliste, neueste zuerst.
/// </summary>
/// <param name="Name">Name der Freizeit.</param>
public sealed record AnschaffungswunschFreizeit(Guid FreizeitId, string Name, IReadOnlyList<SchadenEintrag> Eintraege);

/// <summary>
/// Story 3-4: JSON-Namen und Grenzen der Felder eines Eintrags. <c>PATCH …/inventar/schaeden/{eintragId}</c>: Körper
/// <c>{ "version": n, "alterWert": …, "bezeichnung": neu }</c>, Antwort <c>{ "version": n, "bezeichnung": gespeichert }</c>.
/// </summary>
public static class SchadenFelder
{
    public const string Art = "art";

    public const string Bezeichnung = "bezeichnung";

    public const int BezeichnungMaximaleLaenge = 120;

    public static string JsonName(SchadenFeld feld) => feld switch
    {
        SchadenFeld.Bezeichnung => Bezeichnung,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };
}
