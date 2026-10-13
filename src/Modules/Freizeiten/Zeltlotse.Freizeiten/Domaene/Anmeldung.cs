using Zeltlotse.Freizeiten.Anmeldungen.Entscheiden;
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

/// <summary>Status einer <see cref="Anmeldung"/>; gespeichert als Zahl, nur anhängen. Abgesagt ist endgültig (Story 2-3).</summary>
internal enum AnmeldeStatus : short
{
    Eingegangen = 1,
    Bestaetigt = 2,
    Abgesagt = 3,
}

/// <summary>
/// Anmeldung zu einer Freizeit (FR-16 bis FR-21, AD-7): Freizeiten speichert nur Kennungen, Art, Status und Zeitpunkte; Namen und
/// Mail-Adressen liegen in Personen. Je Freizeit und Person höchstens eine (eindeutiger Index; ausgenommen der Platzhalter
/// gelöschter Personen). Der Status ändert sich nur über Methoden (AD-28): <see cref="Eingehen"/> legt
/// <see cref="AnmeldungEingegangen"/> ab, <see cref="Bestaetigen"/> und <see cref="Absagen"/> legen ihr Ereignis und den
/// Mail-Auftrag <see cref="AnmeldungEntschiedenMail"/> ab (AD-13). Die Anmeldung ist der Datensatz im Umschlag. Arbeitsdaten der
/// Freizeit (AD-16).
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

    /// <summary>Wann die Leitung bestätigt oder abgesagt hat; bei „eingegangen“ leer.</summary>
    public DateTimeOffset? EntschiedenAm { get; private set; }

    /// <summary>
    /// Eine neue Anmeldung „eingegangen“; legt das Ereignis mit dem Ende des Freigabefensters und den Fragen ab, die die Freizeit für die
    /// Art der Anmeldung jetzt abfragt (Story 2-5).
    /// </summary>
    public static Anmeldung Eingehen(
        Guid id, Guid organisationId, Guid freizeitId, Guid personId, AnmeldeArt art, DateTimeOffset jetzt, DateTimeOffset freigabeGueltigBis,
        IReadOnlyList<string> fragen)
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
        anmeldung.EreignisAblegen(new AnmeldungEingegangen(id, freizeitId, personId, Ereignis(art), freigabeGueltigBis, FragenImEreignis.Aus(fragen)));
        return anmeldung;
    }

    /// <summary>
    /// Eingegangen → bestätigt (FR-20): legt <see cref="AnmeldungBestaetigt"/> und die Bestätigungs-Mail ab. Jeder andere Status
    /// bleibt, wie er ist, und das Ergebnis ist <c>false</c> (kein Ereignis, keine Mail). Das Ereignis trägt die Fragen, die die Freizeit für die
    /// Art der Anmeldung jetzt abfragt (Story 2-5).
    /// </summary>
    public bool Bestaetigen(DateTimeOffset jetzt, IReadOnlyList<string> fragen)
    {
        if (Status != AnmeldeStatus.Eingegangen)
        {
            return false;
        }

        Status = AnmeldeStatus.Bestaetigt;
        EntschiedenAm = jetzt;
        EreignisAblegen(new AnmeldungBestaetigt(Id, FreizeitId, PersonId, Ereignis(Art), FragenImEreignis.Aus(fragen)));
        MailVormerken(new AnmeldungEntschiedenMail(Id, Entscheidung.Bestaetigt));
        return true;
    }

    /// <summary>
    /// Eingegangen oder bestätigt → abgesagt (FR-21): legt <see cref="AnmeldungAbgesagt"/> und die Absage-Mail ab. Abgesagt ist
    /// endgültig; ein zweites Absagen ergibt <c>false</c> ohne Ereignis und Mail.
    /// </summary>
    public bool Absagen(DateTimeOffset jetzt)
    {
        if (Status == AnmeldeStatus.Abgesagt)
        {
            return false;
        }

        Status = AnmeldeStatus.Abgesagt;
        EntschiedenAm = jetzt;
        EreignisAblegen(new AnmeldungAbgesagt(Id, FreizeitId, PersonId, Ereignis(Art)));
        MailVormerken(new AnmeldungEntschiedenMail(Id, Entscheidung.Abgesagt));
        return true;
    }

    public static EreignisArt Ereignis(AnmeldeArt art) => art switch
    {
        AnmeldeArt.Teilnehmende => EreignisArt.Teilnehmende,
        AnmeldeArt.Mitarbeitende => EreignisArt.Mitarbeitende,
        _ => throw new ArgumentOutOfRangeException(nameof(art), art, null),
    };
}
