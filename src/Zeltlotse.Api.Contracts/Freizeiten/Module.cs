namespace Zeltlotse.Api.Contracts.Freizeiten;

/// <summary>
/// Antwort von <c>GET /api/freizeiten/{freizeitId}/module</c> (Leitung, FR-44, AD-20): welche Module die Freizeit nutzt, mit Version
/// (AD-19). Eine neue Freizeit nutzt keines. Ein abgeschaltetes Modul antwortet überall mit <c>404</c>; seine Daten bleiben erhalten.
/// </summary>
/// <param name="Verpflegung">Modul Verpflegung (Küche) eingeschaltet.</param>
/// <param name="Finanzen">Modul Finanzen eingeschaltet.</param>
/// <param name="TeilnehmerZugang">Modul Teilnehmer-Zugang (QR-Zugang) eingeschaltet.</param>
public sealed record FreizeitModule(uint Version, bool Verpflegung, bool Finanzen, bool TeilnehmerZugang);

/// <summary>Schaltbare Module für <c>PATCH /api/freizeiten/{freizeitId}/module</c> (AD-19).</summary>
public enum ModulFeld
{
    Verpflegung,
    Finanzen,
    TeilnehmerZugang,
}

/// <summary>
/// JSON-Namen der Module: Körper <c>{ "version": n, "alterWert": "false", "verpflegung"|"finanzen"|"teilnehmerZugang": "true" }</c>,
/// Antwort <c>{ "version": n, "&lt;feld&gt;": "true"|"false" }</c>. Werte sind nur <c>"true"</c> oder <c>"false"</c> (wie beim
/// Autosave-Ankreuzfeld).
/// </summary>
public static class ModulFelder
{
    public const string Verpflegung = "verpflegung";

    public const string Finanzen = "finanzen";

    public const string TeilnehmerZugang = "teilnehmerZugang";

    public static string JsonName(ModulFeld feld) => feld switch
    {
        ModulFeld.Verpflegung => Verpflegung,
        ModulFeld.Finanzen => Finanzen,
        ModulFeld.TeilnehmerZugang => TeilnehmerZugang,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };
}
