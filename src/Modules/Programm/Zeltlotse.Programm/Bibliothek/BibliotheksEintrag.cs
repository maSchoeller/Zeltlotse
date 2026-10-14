using System.Globalization;
using Zeltlotse.Api.Contracts.Programm;
using Zeltlotse.SharedKernel.Mandanten;
using Aufgabe = Zeltlotse.Programm.Aufgaben.Aufgabe;
using Material = Zeltlotse.Programm.Materialien.Material;
using Programmpunkt = Zeltlotse.Programm.Programme.Programmpunkt;

namespace Zeltlotse.Programm.Bibliothek;

/// <summary>
/// Story 3-6: Ein Eintrag der Programmbibliothek der Organisation (CAP-24, FR-41): eine Momentaufnahme eines bewusst abgelegten Programmpunkts
/// mit Titel, Material (Bezeichnung, Menge) und Aufgaben (Titel, Phase). Unveränderlich; der Org-Admin entfernt Einträge.
/// </summary>
/// <remarks>
/// Kein Personenbezug (AD-16): Wer übernommen hat oder mitbringt, Status, Erledigt und Reservierungen gehören der alten Freizeit und kommen nicht
/// mit. Der Eintrag ist Org-Datum und überdauert den Lösch-Lauf der Freizeit, aus der er stammt.
/// </remarks>
internal sealed class BibliotheksEintrag : IOrganisationsEigen
{
    private static readonly StringComparer Vergleich = StringComparer.Create(CultureInfo.GetCultureInfo("de-DE"), ignoreCase: true);

    private BibliotheksEintrag()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    /// <summary>Der Titel des Programmpunkts beim Ablegen.</summary>
    public string Titel { get; private set; } = "";

    public DateTimeOffset AbgelegtAm { get; private set; }

    /// <summary>Das Material nach Bezeichnung (JSON-Spalte).</summary>
    public List<EintragMaterial> Material { get; private set; } = [];

    /// <summary>Die Aufgaben nach Phase und Titel (JSON-Spalte).</summary>
    public List<EintragAufgabe> Aufgaben { get; private set; } = [];

    /// <summary>
    /// Legt einen Programmpunkt ab: Titel, sein Material nach Bezeichnung und die Aufgaben an ihm nach Phase und Titel. Material und Aufgaben
    /// anderer Punkte bleiben außen vor. Mahlzeiten sind feste Punkte jeder Freizeit und lassen sich nicht ablegen.
    /// </summary>
    public static BibliotheksEintrag Ablegen(
        Guid id, Guid organisationId, Programmpunkt punkt, IEnumerable<Material> material, IEnumerable<Aufgabe> aufgaben, DateTimeOffset jetzt)
    {
        ArgumentNullException.ThrowIfNull(punkt);
        if (punkt.Mahlzeit is not null)
        {
            throw new InvalidOperationException("Mahlzeiten lassen sich nicht ablegen.");
        }

        return new BibliotheksEintrag
        {
            Id = id,
            OrganisationId = organisationId,
            Titel = punkt.Titel,
            AbgelegtAm = jetzt,
            Material =
            [
                .. material
                    .Where(m => m.ProgrammpunktId == punkt.Id)
                    .OrderBy(m => m.Bezeichnung, Vergleich)
                    .ThenBy(m => m.Menge, Vergleich)
                    .Select(m => new EintragMaterial(m.Bezeichnung, m.Menge)),
            ],
            Aufgaben =
            [
                .. aufgaben
                    .Where(a => a.ProgrammpunktId == punkt.Id)
                    .OrderBy(a => a.Phase)
                    .ThenBy(a => a.Titel, Vergleich)
                    .Select(a => new EintragAufgabe(a.Titel, a.Phase)),
            ],
        };
    }
}

/// <summary>Story 3-6: Ein Material im Bibliothekseintrag (nur Bezeichnung und Menge).</summary>
internal sealed class EintragMaterial
{
    public EintragMaterial(string bezeichnung, string? menge)
    {
        Bezeichnung = bezeichnung;
        Menge = menge;
    }

    public string Bezeichnung { get; private set; }

    public string? Menge { get; private set; }
}

/// <summary>Story 3-6: Eine Aufgabe im Bibliothekseintrag (nur Titel und Phase).</summary>
internal sealed class EintragAufgabe
{
    public EintragAufgabe(string titel, AufgabenPhase phase)
    {
        Titel = titel;
        Phase = phase;
    }

    public string Titel { get; private set; }

    public AufgabenPhase Phase { get; private set; }
}
