using Zeltlotse.Api.Contracts.Kueche;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Kueche.Rezepte;

/// <summary>
/// Story 4-1: Ein Rezept der Freizeit (CAP-28, FR-46): Name, Basis-Personenzahl und Zubereitung als Freitext. Die Zutaten hängen per Fremdschlüssel
/// im eigenen Schema daran und verschwinden mit dem Rezept (<c>ON DELETE CASCADE</c>). Felder ändern sich nach AD-19 nur über
/// <see cref="FeldAendern"/>.
/// </summary>
/// <remarks>
/// Lösch-Klasse „Arbeitsdaten der Freizeit“ (AD-16) ohne Personenbezug: Nach der Frist für Freizeit-Daten löscht der Lösch-Lauf die Zeile. Die
/// Freitexte nennen keine Namen (Doku).
/// </remarks>
internal sealed class Rezept : IOrganisationsEigen
{
    private Rezept()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid FreizeitId { get; private set; }

    /// <summary>Getrimmt, 1 bis <see cref="RezeptFelder.NameMaximaleLaenge"/> Zeichen.</summary>
    public string Name { get; private set; } = "";

    /// <summary>Für so viele Personen gilt das Rezept, 1 bis <see cref="RezeptFelder.BasisMaximum"/>.</summary>
    public int BasisPersonen { get; private set; }

    /// <summary>Freitext, getrimmt, höchstens <see cref="RezeptFelder.ZubereitungMaximaleLaenge"/> Zeichen; ohne Zubereitung <c>null</c>.</summary>
    public string? Zubereitung { get; private set; }

    /// <summary>Versionsnummer der Zeile (<c>xmin</c>, AD-19).</summary>
    public uint Version { get; private set; }

    public static Rezept Anlegen(Guid id, Guid organisationId, Guid freizeitId, string name, int basisPersonen) => new()
    {
        Id = id,
        OrganisationId = organisationId,
        FreizeitId = freizeitId,
        Name = name.Trim(),
        BasisPersonen = basisPersonen,
    };

    /// <summary>Der Wert eines Felds als Text, wie die API ihn liefert (die Basis ohne Nachkommastellen).</summary>
    public string? Wert(RezeptFeld feld) => feld switch
    {
        RezeptFeld.Name => Name,
        RezeptFeld.BasisPersonen => KuecheWerte.BasisText(BasisPersonen),
        RezeptFeld.Zubereitung => Zubereitung,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };

    /// <summary>
    /// Ändert ein Feld nach der Konfliktregel aus AD-19: Weicht der aktuelle Wert vom mitgeschickten alten Wert ab (beide normalisiert), bleibt alles,
    /// wie es ist, und das Ergebnis ist <c>false</c>. Die Form des neuen Werts prüft der Validator.
    /// </summary>
    public bool FeldAendern(RezeptFeld feld, string? alterWert, string? neuerWert)
    {
        if (Wert(feld) != Vergleichswert(feld, alterWert))
        {
            return false;
        }

        switch (feld)
        {
            case RezeptFeld.Name:
                Name = (neuerWert ?? "").Trim();
                break;
            case RezeptFeld.BasisPersonen:
                BasisPersonen = KuecheWerte.BasisLesen(neuerWert) ?? throw new ArgumentException("Ungültige Basis-Personenzahl.", nameof(neuerWert));
                break;
            case RezeptFeld.Zubereitung:
                Zubereitung = KuecheWerte.TextOderNull(neuerWert);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(feld), feld, null);
        }

        return true;
    }

    /// <summary>Der alte Wert in derselben Form wie <see cref="Wert"/>: getrimmt, leer = <c>null</c>, eine lesbare Basis normalisiert.</summary>
    private static string? Vergleichswert(RezeptFeld feld, string? text) =>
        feld == RezeptFeld.BasisPersonen && KuecheWerte.BasisLesen(text) is { } basis ? KuecheWerte.BasisText(basis) : KuecheWerte.TextOderNull(text);
}
