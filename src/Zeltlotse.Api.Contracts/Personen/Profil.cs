namespace Zeltlotse.Api.Contracts.Personen;

/// <summary>Antwort von <c>GET</c> und <c>POST /api/ich/profil</c>: das eigene Profil mit seiner Version (AD-19).</summary>
public sealed record ProfilAntwort(uint Version, string? Vorname, string? Nachname);

/// <summary>Bearbeitbare Felder des Profils für <c>PATCH /api/ich/profil</c> (AD-19); JSON-Namen in <see cref="ProfilFelder"/>.</summary>
public enum ProfilFeld
{
    Vorname,
    Nachname,
}

/// <summary>
/// JSON-Namen der Felder für <c>PATCH /api/ich/profil</c>: Körper <c>{ "version": n, "alterWert": …, "&lt;feld&gt;": neu }</c>,
/// Antwort <c>{ "version": n, "&lt;feld&gt;": gespeichert }</c>. Je Feld höchstens <see cref="MaximaleLaenge"/> Zeichen.
/// </summary>
public static class ProfilFelder
{
    public const string Vorname = "vorname";

    public const string Nachname = "nachname";

    public const int MaximaleLaenge = 100;

    public static string JsonName(ProfilFeld feld) => feld switch
    {
        ProfilFeld.Vorname => Vorname,
        ProfilFeld.Nachname => Nachname,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };
}
