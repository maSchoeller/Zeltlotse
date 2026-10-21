using Zeltlotse.Api.Contracts.Kanaele;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Kanaele.Nachrichten;

/// <summary>
/// Story 6-1 (CAP-35, FR-61): Eine Kurznachricht in einem der zwei festen Kanäle einer Freizeit. Sie wird nie bearbeitet und nie einzeln gelöscht. Beim
/// Senden merkt sie einen Push vor (<see cref="NachrichtPush"/>, AD-13): Er geht nur über die Outbox hinaus, und sein Inhalt entsteht erst beim Versand.
/// </summary>
/// <remarks>
/// Lösch-Klasse „Arbeitsdaten der Freizeit“ (AD-16): Bei <c>PersonGeloescht</c> wird <see cref="AutorPersonId"/> zum Platzhalter, der Text bleibt; nach
/// der Frist der Organisation löscht der Lösch-Lauf die Nachricht.
/// </remarks>
internal sealed class Nachricht : Aggregat, IOrganisationsEigen
{
    private Nachricht()
    {
    }

    public Guid OrganisationId { get; private set; }

    /// <summary>Reihenfolge des Speicherns (von PostgreSQL vergeben); ordnet Nachrichten mit demselben Zeitpunkt.</summary>
    public long Laufnummer { get; private set; }

    public Guid FreizeitId { get; private set; }

    public Kanal Kanal { get; private set; }

    /// <summary>Wer geschrieben hat (Leitung oder Team); nach dem Löschen dieser Person der Platzhalter <c>GeloeschtePerson.Id</c>.</summary>
    public Guid AutorPersonId { get; private set; }

    public string Text { get; private set; } = "";

    public DateTimeOffset GesendetAm { get; private set; }

    /// <summary>Die bestätigte Nachricht; der Push an die Abos von Leitung und Team ist damit vorgemerkt.</summary>
    public static Nachricht Senden(Guid id, Guid organisationId, Guid freizeitId, Kanal kanal, Guid autorPersonId, string text, DateTimeOffset jetzt)
    {
        var nachricht = new Nachricht
        {
            Id = id,
            OrganisationId = organisationId,
            FreizeitId = freizeitId,
            Kanal = kanal,
            AutorPersonId = autorPersonId,
            Text = text,
            GesendetAm = jetzt,
        };
        nachricht.PushVormerken(new NachrichtPush(id));
        if (kanal == Kanal.Alle)
        {
            // Story 6-4: „Alle“ erreicht auch die QR-Gäste mit Push-Abo (der Aufbau prüft Teilnehmer-Zugang und Freizeitende erst beim Versand).
            nachricht.PushVormerken(new NachrichtGastPush(id));
        }

        return nachricht;
    }
}
