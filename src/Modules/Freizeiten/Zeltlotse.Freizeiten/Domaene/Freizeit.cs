using Zeltlotse.Freizeiten.Contracts.Events;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Mandanten;
using RolleArt = Zeltlotse.SharedKernel.Rechte.FreizeitRolle;

namespace Zeltlotse.Freizeiten.Domaene;

/// <summary>
/// Freizeit (FR-6): Name, Zeitraum und das beim Anlegen festgeschriebene Freigabe-Intervall (FR-24d). Der Abschluss
/// (<see cref="AbgeschlossenAm"/>) ist nur ein Feld; diese Version setzt ihn nicht. Vergaben der Leitung legen das Ereignis
/// <see cref="FreizeitRolleGeaendert"/> ab (AD-28); die Freizeit ist der Datensatz im Umschlag.
/// </summary>
internal sealed class Freizeit : Aggregat, IOrganisationsEigen
{
    public const int MaximaleNamensLaenge = 100;

    public const int FruehestesJahr = 2000;

    public const int SpaetestesJahr = 2100;

    private readonly List<FreizeitRolle> _rollen = [];

    private Freizeit()
    {
    }

    public Guid OrganisationId { get; private init; }

    /// <summary>Getrimmt, 1 bis <see cref="MaximaleNamensLaenge"/> Zeichen.</summary>
    public string Name { get; private init; } = "";

    public DateOnly ErsterTag { get; private init; }

    /// <summary>Gleich oder nach <see cref="ErsterTag"/>.</summary>
    public DateOnly LetzterTag { get; private init; }

    /// <summary>Freigabe-Intervall in Tagen, beim Anlegen festgeschrieben; eine spätere Richtlinie ändert es nicht.</summary>
    public int FreigabeIntervallTage { get; private init; }

    public DateTimeOffset AngelegtAm { get; private init; }

    /// <summary>Die anlegende Person.</summary>
    public Guid AngelegtVon { get; private init; }

    /// <summary>Abschluss durch die Leitung; danach ist die Freizeit „gelaufen“ (AD-17).</summary>
    public DateTimeOffset? AbgeschlossenAm { get; private set; }

    public IReadOnlyCollection<FreizeitRolle> Rollen => _rollen;

    public static Freizeit Anlegen(
        Guid id, Guid organisationId, string name, DateOnly ersterTag, DateOnly letzterTag, int freigabeIntervallTage, Guid angelegtVon, DateTimeOffset jetzt) => new()
    {
        Id = id,
        OrganisationId = organisationId,
        Name = name.Trim(),
        ErsterTag = ersterTag,
        LetzterTag = letzterTag,
        FreigabeIntervallTage = freigabeIntervallTage,
        AngelegtVon = angelegtVon,
        AngelegtAm = jetzt,
    };

    /// <summary>
    /// Setzt die Person als Leitung ein und legt das Ereignis ab; <c>false</c>, wenn sie schon Leitung ist (dann kein
    /// zweites Ereignis). Erwartet die geladenen <see cref="Rollen"/>.
    /// </summary>
    public bool LeitungEinsetzen(Guid personId, DateTimeOffset jetzt)
    {
        if (_rollen.Any(r => r.PersonId == personId && r.Rolle == RolleArt.Leitung))
        {
            return false;
        }

        _rollen.Add(FreizeitRolle.Vergeben(OrganisationId, Id, personId, RolleArt.Leitung, jetzt));
        EreignisAblegen(new FreizeitRolleGeaendert(Id, personId, FreizeitRolleArt.Leitung));
        return true;
    }

    public FreizeitPhase Phase(DateTimeOffset jetzt) => FreizeitZeiten.Phase(ErsterTag, LetzterTag, AbgeschlossenAm, jetzt);

    public FreizeitZeitpunkte Zeitpunkte(DateTimeOffset jetzt) => new(
        Phase(jetzt),
        FreizeitZeiten.Freizeitende(LetzterTag),
        FreizeitZeiten.FreigabeGueltigBis(LetzterTag, FreigabeIntervallTage));
}
