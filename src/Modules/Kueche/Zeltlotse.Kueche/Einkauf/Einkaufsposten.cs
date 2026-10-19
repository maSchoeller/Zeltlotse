using System.Globalization;
using Zeltlotse.Api.Contracts.Kueche;
using Zeltlotse.Kueche.Rezepte;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Kueche.Einkauf;

/// <summary>
/// Story 4-2: Ein Hand-Posten der Einkaufsliste (CAP-29, FR-49), z. B. Zutaten für Ersatzportionen oder „Äpfel 3 kg“: Bezeichnung, freiwillige Menge
/// und Einheit wie bei Zutaten und ein freiwilliger Tag der Freizeit; ohne Tag steht er auf der Liste zuletzt. Felder ändern sich nach AD-19 nur über
/// <see cref="FeldAendern"/>.
/// </summary>
/// <remarks>Lösch-Klasse „Arbeitsdaten der Freizeit“ (AD-16) ohne Personenbezug.</remarks>
internal sealed class Einkaufsposten : IOrganisationsEigen
{
    private Einkaufsposten()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid FreizeitId { get; private set; }

    /// <summary>Getrimmt, 1 bis <see cref="EinkaufspostenFelder.BezeichnungMaximaleLaenge"/> Zeichen.</summary>
    public string Bezeichnung { get; private set; } = "";

    /// <summary>Wie die Menge einer Zutat; ohne Menge <c>null</c>.</summary>
    public decimal? Menge { get; private set; }

    /// <summary>Freitext, höchstens <see cref="EinkaufspostenFelder.EinheitMaximaleLaenge"/> Zeichen; ohne Einheit <c>null</c>.</summary>
    public string? Einheit { get; private set; }

    /// <summary>Ein Tag der Freizeit oder <c>null</c>; ob er dazugehört, prüft der Handler.</summary>
    public DateOnly? Tag { get; private set; }

    /// <summary>Versionsnummer der Zeile (<c>xmin</c>, AD-19).</summary>
    public uint Version { get; private set; }

    public static Einkaufsposten Anlegen(Guid id, Guid organisationId, Guid freizeitId, string bezeichnung, decimal? menge, string? einheit, DateOnly? tag) => new()
    {
        Id = id,
        OrganisationId = organisationId,
        FreizeitId = freizeitId,
        Bezeichnung = bezeichnung.Trim(),
        Menge = menge is { } wert ? KuecheWerte.OhneNullen(wert) : null,
        Einheit = KuecheWerte.TextOderNull(einheit),
        Tag = tag,
    };

    /// <summary>Der Wert eines Felds als Text, wie die API ihn liefert (Menge deutsch mit Komma, Tag als <c>JJJJ-MM-TT</c>).</summary>
    public string? Wert(EinkaufspostenFeld feld) => feld switch
    {
        EinkaufspostenFeld.Bezeichnung => Bezeichnung,
        EinkaufspostenFeld.Menge => Menge is { } menge ? ZutatFelder.ZahlText(menge) : null,
        EinkaufspostenFeld.Einheit => Einheit,
        EinkaufspostenFeld.Tag => KuecheWerte.TagText(Tag),
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };

    /// <summary>
    /// Ändert ein Feld nach der Konfliktregel aus AD-19: Weicht der aktuelle Wert vom mitgeschickten alten Wert ab (beide normalisiert), bleibt alles,
    /// wie es ist, und das Ergebnis ist <c>false</c>. Die Form des neuen Werts prüft der Validator, ob der Tag zur Freizeit gehört der Handler.
    /// </summary>
    public bool FeldAendern(EinkaufspostenFeld feld, string? alterWert, string? neuerWert)
    {
        if (Wert(feld) != Vergleichswert(feld, alterWert))
        {
            return false;
        }

        switch (feld)
        {
            case EinkaufspostenFeld.Bezeichnung:
                Bezeichnung = (neuerWert ?? "").Trim();
                break;
            case EinkaufspostenFeld.Menge:
                Menge = KuecheWerte.TextOderNull(neuerWert) is null
                    ? null
                    : KuecheWerte.MengeLesen(neuerWert) ?? throw new ArgumentException("Ungültige Menge.", nameof(neuerWert));
                break;
            case EinkaufspostenFeld.Einheit:
                Einheit = KuecheWerte.TextOderNull(neuerWert);
                break;
            case EinkaufspostenFeld.Tag:
                Tag = KuecheWerte.TextOderNull(neuerWert) is null
                    ? null
                    : KuecheWerte.TagLesen(neuerWert) ?? throw new ArgumentException("Ungültiger Tag.", nameof(neuerWert));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(feld), feld, null);
        }

        return true;
    }

    private static string? Vergleichswert(EinkaufspostenFeld feld, string? text) => feld switch
    {
        EinkaufspostenFeld.Menge when KuecheWerte.MengeLesen(text) is { } menge => ZutatFelder.ZahlText(menge),
        EinkaufspostenFeld.Tag when KuecheWerte.TagLesen(text) is { } tag => tag.ToString(EinkaufspostenFelder.TagFormat, CultureInfo.InvariantCulture),
        _ => KuecheWerte.TextOderNull(text),
    };
}
