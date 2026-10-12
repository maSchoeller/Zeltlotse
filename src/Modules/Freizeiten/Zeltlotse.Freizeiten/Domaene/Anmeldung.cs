using Zeltlotse.Freizeiten.Contracts.Events;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Mandanten;
using EreignisArt = Zeltlotse.Freizeiten.Contracts.Events.AnmeldungArt;

namespace Zeltlotse.Freizeiten.Domaene;

/// <summary>Art einer <see cref="Anmeldung"/>; gespeichert als Zahl, nur anhängen.</summary>
internal enum AnmeldeArt : short
{
    Teilnehmende = 1,
    Mitarbeitende = 2,
}

/// <summary>Status einer <see cref="Anmeldung"/>; gespeichert als Zahl, nur anhängen. Bestätigen und Absagen kommen mit Story 2-3.</summary>
internal enum AnmeldeStatus : short
{
    Eingegangen = 1,
}

/// <summary>
/// Anmeldung zu einer Freizeit (FR-16 bis FR-19, AD-7): Freizeiten speichert nur Kennungen, Art, Status und Zeitpunkte; Namen und
/// Mail-Adressen liegen in Personen. Je Freizeit und Person höchstens eine (eindeutiger Index; ausgenommen der Platzhalter
/// gelöschter Personen). Der Status ändert sich nur über Methoden (AD-28); <see cref="Eingehen"/> legt
/// <see cref="AnmeldungEingegangen"/> ab, die Anmeldung ist der Datensatz im Umschlag. Arbeitsdaten der Freizeit (AD-16).
/// </summary>
internal sealed class Anmeldung : Aggregat, IOrganisationsEigen
{
    private Anmeldung()
    {
    }

    public Guid OrganisationId { get; private init; }

    public Guid FreizeitId { get; private init; }

    /// <summary>Die angemeldete Person; nach ihrer Löschung der Platzhalter <c>GeloeschtePerson</c>.</summary>
    public Guid PersonId { get; private set; }

    public AnmeldeArt Art { get; private init; }

    public AnmeldeStatus Status { get; private set; }

    public DateTimeOffset EingegangenAm { get; private init; }

    /// <summary>Wann die Teilnahmebedingungen der Freizeit digital bestätigt wurden (FR-19).</summary>
    public DateTimeOffset TeilnahmebedingungenBestaetigtAm { get; private init; }

    /// <summary>Eine neue Anmeldung „eingegangen“; legt das Ereignis mit dem Ende des Freigabefensters ab.</summary>
    public static Anmeldung Eingehen(
        Guid id, Guid organisationId, Guid freizeitId, Guid personId, AnmeldeArt art, DateTimeOffset jetzt, DateTimeOffset freigabeGueltigBis)
    {
        var anmeldung = new Anmeldung
        {
            Id = id,
            OrganisationId = organisationId,
            FreizeitId = freizeitId,
            PersonId = personId,
            Art = art,
            Status = AnmeldeStatus.Eingegangen,
            EingegangenAm = jetzt,
            TeilnahmebedingungenBestaetigtAm = jetzt,
        };
        anmeldung.EreignisAblegen(new AnmeldungEingegangen(id, freizeitId, personId, Ereignis(art), freigabeGueltigBis));
        return anmeldung;
    }

    public static EreignisArt Ereignis(AnmeldeArt art) => art switch
    {
        AnmeldeArt.Teilnehmende => EreignisArt.Teilnehmende,
        AnmeldeArt.Mitarbeitende => EreignisArt.Mitarbeitende,
        _ => throw new ArgumentOutOfRangeException(nameof(art), art, null),
    };
}
