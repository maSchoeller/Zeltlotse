using System.Globalization;
using Zeltlotse.Api.Contracts.Organisationen;
using Zeltlotse.SharedKernel.Mandanten;
using NachweisArt = Zeltlotse.Personen.Contracts.Events.NachweisArt;

namespace Zeltlotse.Organisationen.Nachweise;

/// <summary>
/// Richtlinie der Organisation für eine Nachweisart (FR-9, NFR-5): ob sie Pflicht ist und wie viele Monate ab Ausstellung
/// eine Bestätigung gilt. Je Organisation und Art eine Zeile; die vier Zeilen entstehen mit der Organisation
/// (<see cref="Vorgaben"/>). Bearbeiten nach AD-19.
/// </summary>
internal sealed class NachweisRichtlinie : IOrganisationsEigen
{
    /// <summary>Vorgabewerte aus der Praxis der Jugendarbeit; nur das Führungszeugnis ist von Haus aus Pflicht.</summary>
    public static readonly IReadOnlyList<(NachweisArt Art, bool Pflicht, int GueltigkeitMonate)> Vorgaben =
    [
        (NachweisArt.Fuehrungszeugnis, true, 60),
        (NachweisArt.Juleica, false, 36),
        (NachweisArt.ErsteHilfe, false, 24),
        (NachweisArt.Praeventionsschulung, false, 60),
    ];

    private NachweisRichtlinie()
    {
    }

    public Guid OrganisationId { get; private init; }

    public NachweisArt Art { get; private init; }

    public bool Pflicht { get; private set; }

    /// <summary>Zwischen <see cref="RichtlinieFelder.GueltigkeitMindestens"/> und <see cref="RichtlinieFelder.GueltigkeitHoechstens"/>.</summary>
    public int GueltigkeitMonate { get; private set; }

    /// <summary>Versionsprüfung (xmin, AD-19).</summary>
    public uint Version { get; private set; }

    /// <summary>Die vier Vorgabezeilen einer neuen Organisation.</summary>
    public static IEnumerable<NachweisRichtlinie> VorgabenFuer(Guid organisationId) =>
        Vorgaben.Select(v => new NachweisRichtlinie
        {
            OrganisationId = organisationId,
            Art = v.Art,
            Pflicht = v.Pflicht,
            GueltigkeitMonate = v.GueltigkeitMonate,
        });

    /// <summary>Die Vorgabe-Gültigkeit einer Art, falls die Zeile fehlt.</summary>
    public static int VorgabeGueltigkeit(NachweisArt art) => Vorgaben.Single(v => v.Art == art).GueltigkeitMonate;

    /// <summary>Wert eines Felds als Text: <c>"true"</c>/<c>"false"</c> bzw. die Zahl der Monate.</summary>
    public string Wert(RichtlinieFeld feld) => feld switch
    {
        RichtlinieFeld.Pflicht => Pflicht ? "true" : "false",
        RichtlinieFeld.GueltigkeitMonate => GueltigkeitMonate.ToString(CultureInfo.InvariantCulture),
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };

    /// <summary>
    /// Ändert ein Feld nach der Konfliktregel aus AD-19 (alter Wert desselben Felds muss passen, sonst <c>false</c>). Die
    /// Formprüfung hat der Validator erledigt.
    /// </summary>
    public bool FeldAendern(RichtlinieFeld feld, string? alterWert, string neuerWert)
    {
        if (!string.Equals(Wert(feld), alterWert?.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        switch (feld)
        {
            case RichtlinieFeld.Pflicht:
                Pflicht = neuerWert.Trim() == "true";
                break;
            case RichtlinieFeld.GueltigkeitMonate:
                GueltigkeitMonate = int.Parse(neuerWert.Trim(), NumberStyles.None, CultureInfo.InvariantCulture);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(feld), feld, null);
        }

        return true;
    }
}

/// <summary>
/// Bestätigung eines Nachweises bei der Organisation (AD-9): nur Art, eingesehen am, gültig bis und von wem. Keine Datei, kein
/// Ausstellungsdatum. Eindeutig je (Organisation, Person, Art); die jüngste Einsicht gewinnt. Entsteht nur aus dem Ereignis
/// <c>NachweisEingesehen</c>.
/// </summary>
internal sealed class NachweisBestaetigung : IOrganisationsEigen
{
    private NachweisBestaetigung()
    {
    }

    public Guid OrganisationId { get; private init; }

    public Guid PersonId { get; private init; }

    public NachweisArt Art { get; private init; }

    public DateTimeOffset EingesehenAm { get; private set; }

    /// <summary>Berechnet aus Ausstellungsdatum und Gültigkeit der Richtlinie, nie von Hand gesetzt.</summary>
    public DateOnly GueltigBis { get; private set; }

    /// <summary>Die prüfende Person (PersonId).</summary>
    public Guid GeprueftVon { get; private set; }

    public static NachweisBestaetigung Anlegen(Guid organisationId, Guid personId, NachweisArt art, DateTimeOffset eingesehenAm, DateOnly gueltigBis, Guid geprueftVon) => new()
    {
        OrganisationId = organisationId,
        PersonId = personId,
        Art = art,
        EingesehenAm = eingesehenAm,
        GueltigBis = gueltigBis,
        GeprueftVon = geprueftVon,
    };

    /// <summary>Übernimmt eine jüngere Einsicht; eine ältere ändert nichts.</summary>
    public void Uebernehmen(DateTimeOffset eingesehenAm, DateOnly gueltigBis, Guid geprueftVon)
    {
        if (eingesehenAm < EingesehenAm)
        {
            return;
        }

        EingesehenAm = eingesehenAm;
        GueltigBis = gueltigBis;
        GeprueftVon = geprueftVon;
    }
}
