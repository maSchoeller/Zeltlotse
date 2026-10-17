using Zeltlotse.Api.Contracts.Kueche;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Kueche.Rezepte;

/// <summary>
/// Story 4-1: Eine Zutat eines Rezepts (FR-46): Bezeichnung, Menge für die Basis-Personenzahl (ohne Menge, z. B. „Salz“, <c>null</c>), Einheit als
/// Freitext ohne Umrechnung und Allergene als Komma-Liste der Schlüssel der Allergie-Frage (AD-10), in Katalogreihenfolge ohne Doppelte. Felder
/// ändern sich nach AD-19 nur über <see cref="FeldAendern"/>; die Reihenfolge im Rezept ist die der Kennungen.
/// </summary>
internal sealed class Zutat : IOrganisationsEigen
{
    private Zutat()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid FreizeitId { get; private set; }

    public Guid RezeptId { get; private set; }

    /// <summary>Getrimmt, 1 bis <see cref="ZutatFelder.BezeichnungMaximaleLaenge"/> Zeichen.</summary>
    public string Bezeichnung { get; private set; } = "";

    /// <summary>Größer 0, höchstens <see cref="ZutatFelder.MengeMaximum"/>, höchstens 3 Nachkommastellen; ohne Menge <c>null</c>.</summary>
    public decimal? Menge { get; private set; }

    /// <summary>Freitext, höchstens <see cref="ZutatFelder.EinheitMaximaleLaenge"/> Zeichen; ohne Einheit <c>null</c>.</summary>
    public string? Einheit { get; private set; }

    /// <summary>Komma-Liste der Allergen-Schlüssel in Katalogreihenfolge; ohne Allergene <c>null</c>.</summary>
    public string? Allergene { get; private set; }

    /// <summary>Versionsnummer der Zeile (<c>xmin</c>, AD-19).</summary>
    public uint Version { get; private set; }

    public static Zutat Anlegen(Guid id, Guid organisationId, Guid freizeitId, Guid rezeptId, string bezeichnung, decimal? menge, string? einheit) => new()
    {
        Id = id,
        OrganisationId = organisationId,
        FreizeitId = freizeitId,
        RezeptId = rezeptId,
        Bezeichnung = bezeichnung.Trim(),
        Menge = menge is { } wert ? KuecheWerte.OhneNullen(wert) : null,
        Einheit = KuecheWerte.TextOderNull(einheit),
    };

    /// <summary>Der Wert eines Felds als Text, wie die API ihn liefert (die Menge deutsch mit Komma).</summary>
    public string? Wert(ZutatFeld feld) => feld switch
    {
        ZutatFeld.Bezeichnung => Bezeichnung,
        ZutatFeld.Menge => Menge is { } menge ? ZutatFelder.ZahlText(menge) : null,
        ZutatFeld.Einheit => Einheit,
        ZutatFeld.Allergene => Allergene,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };

    /// <summary>
    /// Ändert ein Feld nach der Konfliktregel aus AD-19: Weicht der aktuelle Wert vom mitgeschickten alten Wert ab (beide normalisiert, „1.5“ passt zu
    /// „1,5“), bleibt alles, wie es ist, und das Ergebnis ist <c>false</c>. Die Form des neuen Werts prüft der Validator.
    /// </summary>
    public bool FeldAendern(ZutatFeld feld, string? alterWert, string? neuerWert)
    {
        if (Wert(feld) != Vergleichswert(feld, alterWert))
        {
            return false;
        }

        switch (feld)
        {
            case ZutatFeld.Bezeichnung:
                Bezeichnung = (neuerWert ?? "").Trim();
                break;
            case ZutatFeld.Menge:
                Menge = KuecheWerte.TextOderNull(neuerWert) is null
                    ? null
                    : KuecheWerte.MengeLesen(neuerWert) ?? throw new ArgumentException("Ungültige Menge.", nameof(neuerWert));
                break;
            case ZutatFeld.Einheit:
                Einheit = KuecheWerte.TextOderNull(neuerWert);
                break;
            case ZutatFeld.Allergene:
                Allergene = KuecheWerte.AllergeneNormalisieren(neuerWert);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(feld), feld, null);
        }

        return true;
    }

    /// <summary>Der alte Wert in derselben Form wie <see cref="Wert"/>.</summary>
    private static string? Vergleichswert(ZutatFeld feld, string? text) => feld switch
    {
        ZutatFeld.Menge when KuecheWerte.MengeLesen(text) is { } menge => ZutatFelder.ZahlText(menge),
        ZutatFeld.Allergene => KuecheWerte.AllergeneNormalisieren(text),
        _ => KuecheWerte.TextOderNull(text),
    };
}
