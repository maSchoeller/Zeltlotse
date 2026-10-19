using Zeltlotse.Kueche.Rezepte;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Kueche.Einkauf;

/// <summary>Story 4-3: Die zwei Ja/Nein-Felder eines Postens der Einkaufsliste.</summary>
internal enum StandFeld
{
    Freigegeben,
    Gekauft,
}

/// <summary>
/// Story 4-3 (CAP-30, FR-51): Der Stand eines berechneten Postens (Gericht × Zutat) der Einkaufsliste: von der Küche zum Einkaufen freigegeben und von
/// den Einkaufenden abgehakt. Die Liste selbst bleibt berechnet (AD-13); gespeichert wird nur, was Menschen entscheiden. Ohne Zeile gilt ein Posten als
/// nicht freigegeben und nicht gekauft; die Zeile entsteht beim ersten Ändern, höchstens eine je (Gericht, Zutat), und verschwindet mit dem Gericht oder
/// der Zutat (Fremdschlüssel im eigenen Schema, <c>ON DELETE CASCADE</c>). Beide Felder sind voneinander unabhängig. Wer abgehakt hat, steht nirgends
/// (keine Personendaten in der Küche).
/// </summary>
/// <remarks>Lösch-Klasse „Arbeitsdaten der Freizeit“ (AD-16) ohne Personenbezug.</remarks>
internal sealed class PostenStand : IOrganisationsEigen
{
    private PostenStand()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid FreizeitId { get; private set; }

    public Guid GerichtId { get; private set; }

    public Guid ZutatId { get; private set; }

    public bool Freigegeben { get; private set; }

    public bool Gekauft { get; private set; }

    /// <summary>Versionsnummer der Zeile (<c>xmin</c>, AD-19).</summary>
    public uint Version { get; private set; }

    /// <summary>Ein neuer Stand: nicht freigegeben, nicht gekauft.</summary>
    public static PostenStand Anlegen(Guid id, Guid organisationId, Guid freizeitId, Guid gerichtId, Guid zutatId) => new()
    {
        Id = id,
        OrganisationId = organisationId,
        FreizeitId = freizeitId,
        GerichtId = gerichtId,
        ZutatId = zutatId,
    };

    /// <summary>Der Wert eines Felds als <c>"true"</c>/<c>"false"</c>; ohne Zeile <c>"false"</c>.</summary>
    public static string Wert(PostenStand? stand, StandFeld feld) => KuecheWerte.JaNeinText(feld switch
    {
        StandFeld.Freigegeben => stand?.Freigegeben ?? false,
        StandFeld.Gekauft => stand?.Gekauft ?? false,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    });

    /// <summary>
    /// Ändert ein Feld nach der Konfliktregel aus AD-19: Weicht der aktuelle Wert vom mitgeschickten alten Wert ab, bleibt alles, wie es ist, und das
    /// Ergebnis ist <c>false</c>. Die Form des neuen Werts (<c>"true"</c>/<c>"false"</c>) prüft der Validator.
    /// </summary>
    public bool FeldAendern(StandFeld feld, string? alterWert, string? neuerWert)
    {
        if (!KuecheWerte.JaNeinGleich(Wert(this, feld), alterWert))
        {
            return false;
        }

        var neu = KuecheWerte.JaNeinLesen(neuerWert) ?? throw new ArgumentException("Nur „true“ oder „false“.", nameof(neuerWert));
        if (feld == StandFeld.Freigegeben)
        {
            Freigegeben = neu;
        }
        else
        {
            Gekauft = neu;
        }

        return true;
    }

    /// <summary>Tages-Freigabe: setzt nur <see cref="Freigegeben"/>; <see cref="Gekauft"/> bleibt.</summary>
    public void Freigeben(bool freigegeben) => Freigegeben = freigegeben;
}
