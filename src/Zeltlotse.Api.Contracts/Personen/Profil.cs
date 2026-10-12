namespace Zeltlotse.Api.Contracts.Personen;

/// <summary>Antwort von <c>GET</c> und <c>POST /api/ich/profil</c>: das eigene Profil mit seiner Version (AD-19).</summary>
/// <param name="Herkunft">Freiwillig: Wohnort, erscheint auf der Freizeitseite jeder Freizeit, in der die Person mitarbeitet (FR-15).</param>
public sealed record ProfilAntwort(uint Version, string? Vorname, string? Nachname, string? Herkunft);

/// <summary>Bearbeitbare Felder des Profils für <c>PATCH /api/ich/profil</c> (AD-19); JSON-Namen in <see cref="ProfilFelder"/>.</summary>
public enum ProfilFeld
{
    Vorname,
    Nachname,
    Herkunft,
}

/// <summary>
/// JSON-Namen der Felder für <c>PATCH /api/ich/profil</c>: Körper <c>{ "version": n, "alterWert": …, "&lt;feld&gt;": neu }</c>,
/// Antwort <c>{ "version": n, "&lt;feld&gt;": gespeichert }</c>. Je Feld höchstens <see cref="MaximaleLaenge"/> Zeichen, die
/// Herkunft höchstens <see cref="HerkunftMaximaleLaenge"/>.
/// </summary>
public static class ProfilFelder
{
    public const string Vorname = "vorname";

    public const string Nachname = "nachname";

    public const string Herkunft = "herkunft";

    public const int MaximaleLaenge = 100;

    public const int HerkunftMaximaleLaenge = 60;

    /// <summary>Höchstlänge eines Felds.</summary>
    public static int Laenge(ProfilFeld feld) => feld == ProfilFeld.Herkunft ? HerkunftMaximaleLaenge : MaximaleLaenge;

    public static string JsonName(ProfilFeld feld) => feld switch
    {
        ProfilFeld.Vorname => Vorname,
        ProfilFeld.Nachname => Nachname,
        ProfilFeld.Herkunft => Herkunft,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };
}
