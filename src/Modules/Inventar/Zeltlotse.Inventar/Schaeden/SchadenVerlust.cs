using Zeltlotse.Api.Contracts.Inventar;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Inventar.Schaeden;

/// <summary>
/// Story 3-4: Ein Eintrag auf der Schadens- und Verlustliste einer Freizeit (FR-32, FR-38): was kaputtgegangen, verloren gegangen oder aus dem
/// Erste-Hilfe-Kasten entnommen ist. Nach dem Abschluss der Freizeit ist er ein Anschaffungswunsch an die Organisation (abgeleitet aus der Phase,
/// keine eigene Zeile). Die Bezeichnung ändert sich nach AD-19 über <see cref="FeldAendern"/>. Kommt der Eintrag aus dem Vorfall-Protokoll, trägt er
/// nur die Kennung des Vorfall-Eintrags, nie dessen Text (AD-14). Kein Personenbezug.
/// </summary>
internal sealed class SchadenVerlust : IOrganisationsEigen
{
    /// <summary>Bezeichnung eines Eintrags aus dem Vorfall-Protokoll; der Text der Entnahme bleibt in Sicherheit.</summary>
    public const string ErsteHilfeKasten = "Erste-Hilfe-Kasten";

    private SchadenVerlust()
    {
    }

    public Guid Id { get; private init; }

    public Guid OrganisationId { get; private init; }

    public Guid FreizeitId { get; private init; }

    public SchadenArt Art { get; private init; }

    /// <summary>Getrimmt, 1 bis <see cref="SchadenFelder.BezeichnungMaximaleLaenge"/> Zeichen.</summary>
    public string Bezeichnung { get; private set; } = "";

    public DateTimeOffset ErfasstAm { get; private init; }

    /// <summary>Der Vorfall-Eintrag (Kennung aus Sicherheit), aus dem der Eintrag entstand, sonst <c>null</c>; je Kennung höchstens ein Eintrag.</summary>
    public Guid? VorfallEintragId { get; private init; }

    /// <summary>Versionsnummer der Zeile (<c>xmin</c>, AD-19).</summary>
    public uint Version { get; private set; }

    /// <summary>Ein von Hand eingetragener Schaden, Verlust oder eine Entnahme.</summary>
    public static SchadenVerlust Eintragen(Guid id, Guid organisationId, Guid freizeitId, SchadenArt art, string bezeichnung, DateTimeOffset erfasstAm) => new()
    {
        Id = id,
        OrganisationId = organisationId,
        FreizeitId = freizeitId,
        Art = art,
        Bezeichnung = bezeichnung.Trim(),
        ErfasstAm = erfasstAm,
    };

    /// <summary>Eine Entnahme aus dem Vorfall-Protokoll: Bezeichnung „Erste-Hilfe-Kasten“, der Text bleibt beim Vorfall-Eintrag.</summary>
    public static SchadenVerlust AusEntnahme(Guid id, Guid organisationId, Guid freizeitId, Guid vorfallEintragId, DateTimeOffset erfasstAm) => new()
    {
        Id = id,
        OrganisationId = organisationId,
        FreizeitId = freizeitId,
        Art = SchadenArt.Entnahme,
        Bezeichnung = ErsteHilfeKasten,
        ErfasstAm = erfasstAm,
        VorfallEintragId = vorfallEintragId,
    };

    /// <summary>Der Wert eines Felds als Text, wie die API ihn liefert.</summary>
    public string? Wert(SchadenFeld feld) => feld switch
    {
        SchadenFeld.Bezeichnung => Bezeichnung,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };

    /// <summary>
    /// Ändert ein Feld nach der Konfliktregel aus AD-19: Weicht der aktuelle Wert vom mitgeschickten alten Wert ab (beide getrimmt), bleibt alles,
    /// wie es ist, und das Ergebnis ist <c>false</c>. Die Form prüft der Validator.
    /// </summary>
    public bool FeldAendern(SchadenFeld feld, string? alterWert, string? neuerWert)
    {
        if (Wert(feld) != (alterWert ?? "").Trim())
        {
            return false;
        }

        Bezeichnung = (neuerWert ?? "").Trim();
        return true;
    }
}
