using Zeltlotse.Api.Contracts.Kueche;
using Zeltlotse.Kueche.Rezepte;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Kueche.Einkauf;

/// <summary>
/// Story 4-4 (CAP-31, FR-53): Der Stand einer Mahlzeit in der Küche: als Restetag markiert oder nicht. Die Mahlzeit gehört dem Modul Programm; die Küche
/// verweist nur per Kennung auf den Programmpunkt (AD-3) und speichert ihren Stand selbst. Ohne Zeile ist eine Mahlzeit kein Restetag; die Zeile entsteht
/// beim ersten Ändern, höchstens eine je (Freizeit, Programmpunkt). Der Restetag ist eine reine Markierung: Gerichte, Einkaufsliste und Einkauf bleiben,
/// wie sie sind (AD-13, Leitprinzip 6).
/// </summary>
/// <remarks>Lösch-Klasse „Arbeitsdaten der Freizeit“ (AD-16) ohne Personenbezug.</remarks>
internal sealed class MahlzeitStand : IOrganisationsEigen
{
    private MahlzeitStand()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid FreizeitId { get; private set; }

    /// <summary>Kennung der Mahlzeit (Programmpunkt des Moduls Programm), ohne Fremdschlüssel über die Schemagrenze.</summary>
    public Guid ProgrammpunktId { get; private set; }

    public bool Restetag { get; private set; }

    /// <summary>Versionsnummer der Zeile (<c>xmin</c>, AD-19).</summary>
    public uint Version { get; private set; }

    /// <summary>Ein neuer Stand: kein Restetag.</summary>
    public static MahlzeitStand Anlegen(Guid id, Guid organisationId, Guid freizeitId, Guid programmpunktId) => new()
    {
        Id = id,
        OrganisationId = organisationId,
        FreizeitId = freizeitId,
        ProgrammpunktId = programmpunktId,
    };

    /// <summary>Der Wert eines Felds als <c>"true"</c>/<c>"false"</c>; ohne Zeile <c>"false"</c>.</summary>
    public static string Wert(MahlzeitStand? stand, MahlzeitFeld feld) => feld switch
    {
        MahlzeitFeld.Restetag => KuecheWerte.JaNeinText(stand?.Restetag ?? false),
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };

    /// <summary>
    /// Ändert ein Feld nach der Konfliktregel aus AD-19: Weicht der aktuelle Wert vom mitgeschickten alten Wert ab, bleibt alles, wie es ist, und das
    /// Ergebnis ist <c>false</c>. Die Form des neuen Werts (<c>"true"</c>/<c>"false"</c>) prüft der Validator.
    /// </summary>
    public bool FeldAendern(MahlzeitFeld feld, string? alterWert, string? neuerWert)
    {
        if (!KuecheWerte.JaNeinGleich(Wert(this, feld), alterWert))
        {
            return false;
        }

        Restetag = KuecheWerte.JaNeinLesen(neuerWert) ?? throw new ArgumentException("Nur „true“ oder „false“.", nameof(neuerWert));
        return true;
    }
}
