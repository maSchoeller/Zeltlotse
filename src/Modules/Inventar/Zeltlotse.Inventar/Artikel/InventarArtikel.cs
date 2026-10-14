using Zeltlotse.Api.Contracts.Inventar;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Inventar.Artikel;

/// <summary>
/// Story 3-3: Ein Artikel im Inventar der Organisation (FR-37): Bezeichnung und optionale Menge als Freitext. Beide Felder ändern sich nach AD-19
/// über <see cref="FeldAendern"/>. Reservierungen hängen am Artikel (Fremdschlüssel im eigenen Schema, ohne Cascade); einen Artikel mit
/// Reservierungen löscht niemand. Kein Personenbezug.
/// </summary>
internal sealed class InventarArtikel : IOrganisationsEigen
{
    private InventarArtikel()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    /// <summary>Getrimmt, 1 bis <see cref="InventarFelder.BezeichnungMaximaleLaenge"/> Zeichen.</summary>
    public string Bezeichnung { get; private set; } = "";

    /// <summary>Freitext, getrimmt, höchstens <see cref="InventarFelder.MengeMaximaleLaenge"/> Zeichen; ohne Menge <c>null</c>.</summary>
    public string? Menge { get; private set; }

    /// <summary>Versionsnummer der Zeile (<c>xmin</c>, AD-19).</summary>
    public uint Version { get; private set; }

    public static InventarArtikel Anlegen(Guid id, Guid organisationId, string bezeichnung, string? menge) => new()
    {
        Id = id,
        OrganisationId = organisationId,
        Bezeichnung = bezeichnung.Trim(),
        Menge = TextOderNull(menge),
    };

    /// <summary>Der Wert eines Felds als Text, wie die API ihn liefert.</summary>
    public string? Wert(InventarArtikelFeld feld) => feld switch
    {
        InventarArtikelFeld.Bezeichnung => Bezeichnung,
        InventarArtikelFeld.Menge => Menge,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };

    /// <summary>
    /// Ändert ein Feld nach der Konfliktregel aus AD-19: Weicht der aktuelle Wert vom mitgeschickten alten Wert ab, bleibt alles, wie es ist,
    /// und das Ergebnis ist <c>false</c>. Beide Felder werden getrimmt; eine leere Menge heißt ohne Menge. Die Form prüft der Validator.
    /// </summary>
    public bool FeldAendern(InventarArtikelFeld feld, string? alterWert, string? neuerWert)
    {
        if (TextOderNull(Wert(feld)) != TextOderNull(alterWert))
        {
            return false;
        }

        switch (feld)
        {
            case InventarArtikelFeld.Bezeichnung:
                Bezeichnung = (neuerWert ?? "").Trim();
                break;
            case InventarArtikelFeld.Menge:
                Menge = TextOderNull(neuerWert);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(feld), feld, null);
        }

        return true;
    }

    /// <summary>Getrimmter Text; leer oder nur Leerzeichen heißt <c>null</c>.</summary>
    private static string? TextOderNull(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
