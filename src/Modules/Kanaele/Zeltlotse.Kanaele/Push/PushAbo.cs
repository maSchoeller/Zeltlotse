using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Kanaele.Push;

/// <summary>
/// Story 6-1 (AD-21): Das Push-Abo eines Browsers für eine Freizeit. Es gehört Kanäle. Je Freizeit und Endpunkt gibt es höchstens eines (eindeutiger Index);
/// sendet ein Browser sein Abo erneut, auch nach einem Wechsel der Person am selben Gerät, wird es überschrieben.
/// </summary>
/// <remarks>
/// Lösch-Klasse „personeneigen“ (AD-16): Bei <c>PersonGeloescht</c> wird das Abo sofort gelöscht, sonst mit den Daten der Freizeit im Lösch-Lauf. Ein Abo,
/// das der Push-Dienst nicht mehr kennt (404/410), räumt die Zustellung ab.
/// </remarks>
internal sealed class PushAbo : IOrganisationsEigen
{
    private PushAbo()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid FreizeitId { get; private set; }

    /// <summary>Wer das Abo angelegt hat; Pushes gehen nur an Abos von Personen, die gerade Leitung oder Team der Freizeit sind.</summary>
    public Guid PersonId { get; private set; }

    /// <summary>Adresse beim Push-Dienst (nur bekannte Push-Dienste, siehe <c>PushEndpunkte</c>).</summary>
    public string Endpunkt { get; private set; } = "";

    public string P256dh { get; private set; } = "";

    public string Auth { get; private set; } = "";

    public DateTimeOffset AngelegtAm { get; private set; }

    /// <summary>Nie Endpunkt, Schlüssel oder Person ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(PushAbo)}({Id})";
}
