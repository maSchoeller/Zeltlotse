using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Finanzen.Beitraege;

/// <summary>
/// Story 5-3 (CAP-34, FR-60): Der Vermerk „Beitrag bezahlt“ einer teilnehmenden Person (<see cref="PersonId"/>, AD-7) in einer Freizeit: wer ihn gesetzt hat
/// (<see cref="AbgehaktVon"/>) und wann. Höchstens ein Vermerk je Freizeit und Person (eindeutiger Index ohne den Platzhalter). „Offen“ hat keinen Vermerk:
/// Wieder öffnen löscht ihn endgültig. Ein Vermerk wird nie bearbeitet und hat darum keine Version.
/// </summary>
/// <remarks>
/// Lösch-Klasse „aufbewahrungspflichtig für die Organisation“ (AD-16): Bei <c>PersonGeloescht</c> werden <see cref="PersonId"/> und <see cref="AbgehaktVon"/>
/// der gelöschten Person zum Platzhalter, der Rest bleibt; nach der Frist der Organisation löscht der Lösch-Lauf den Vermerk.
/// </remarks>
internal sealed class BeitragVermerk : IOrganisationsEigen
{
    private BeitragVermerk()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid FreizeitId { get; private set; }

    /// <summary>Die teilnehmende Person; nach ihrem Löschen der Platzhalter <c>GeloeschtePerson.Id</c>.</summary>
    public Guid PersonId { get; private set; }

    /// <summary>Wer den Haken gesetzt hat (Leitung oder Team); nach dem Löschen dieser Person der Platzhalter.</summary>
    public Guid AbgehaktVon { get; private set; }

    /// <summary>Wann der Haken gesetzt wurde (Uhr des Servers).</summary>
    public DateTimeOffset AbgehaktAm { get; private set; }

    public static BeitragVermerk Anlegen(Guid id, Guid organisationId, Guid freizeitId, Guid personId, Guid abgehaktVon, DateTimeOffset jetzt) => new()
    {
        Id = id,
        OrganisationId = organisationId,
        FreizeitId = freizeitId,
        PersonId = personId,
        AbgehaktVon = abgehaktVon,
        AbgehaktAm = jetzt,
    };
}
