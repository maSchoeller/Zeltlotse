using Zeltlotse.Api.Contracts.Personen;

namespace Zeltlotse.Personen.Profil;

/// <summary>
/// Person mit Profil (AD-7): Die Kennung ist die <c>PersonId</c>, die Konten beim Anlegen des Kontos vergibt.
/// Nicht organisationseigen. <see cref="Version"/> ist <c>xmin</c> für das Bearbeiten nach AD-19.
/// </summary>
internal sealed class Person
{
    private Person()
    {
    }

    public Guid Id { get; private init; }

    public string? Vorname { get; private set; }

    public string? Nachname { get; private set; }

    /// <summary>Von PostgreSQL bei jeder Änderung weitergezählt (<c>xmin</c>).</summary>
    public uint Version { get; private set; }

    public DateTimeOffset AngelegtAm { get; private init; }

    public static Person Anlegen(Guid personId, DateTimeOffset jetzt) => new() { Id = personId, AngelegtAm = jetzt };

    public string? Wert(ProfilFeld feld) => feld switch
    {
        ProfilFeld.Vorname => Vorname,
        ProfilFeld.Nachname => Nachname,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };

    /// <summary>
    /// Ändert ein Feld nach der Konfliktregel aus AD-19: Weicht der aktuelle Wert vom mitgeschickten alten Wert ab, bleibt
    /// alles, wie es ist, und das Ergebnis ist <c>false</c>. Leere Werte gelten als „nicht angegeben“.
    /// </summary>
    public bool FeldAendern(ProfilFeld feld, string? alterWert, string? neuerWert)
    {
        if (Normalisieren(Wert(feld)) != Normalisieren(alterWert))
        {
            return false;
        }

        var wert = Normalisieren(neuerWert);
        switch (feld)
        {
            case ProfilFeld.Vorname:
                Vorname = wert;
                break;
            case ProfilFeld.Nachname:
                Nachname = wert;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(feld), feld, null);
        }

        return true;
    }

    public static string? Normalisieren(string? wert) => string.IsNullOrWhiteSpace(wert) ? null : wert.Trim();
}
