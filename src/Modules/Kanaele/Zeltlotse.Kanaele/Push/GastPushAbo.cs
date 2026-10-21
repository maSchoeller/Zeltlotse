using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Kanaele.Push;

/// <summary>
/// Story 6-4 (AD-21): Das Push-Abo eines QR-Gasts für den Kanal „Alle“ einer Freizeit. Es gehört Kanäle, wie die Abos von Leitung und Team, hat aber keine
/// Person, nur die Geräte-Kennung aus der Gast-Sitzung; deshalb gibt es keine Überschneidung mit den Team-Abos. Je Freizeit und Endpunkt gibt es höchstens
/// eines; sendet ein Browser sein Abo erneut (auch nach einem neuen Scan), wird es überschrieben.
/// </summary>
/// <remarks>
/// Das Gast-Abo verfällt mit dem QR-Zugang: Gesendet wird nur, solange der Teilnehmer-Zugang an ist, das Freizeitende nicht erreicht ist und der
/// <see cref="Stempel"/> noch der des QR-Links ist; nach „Link erneuern“ bekommt ein altes Gast-Abo also nichts mehr, bis das Gerät neu scannt. Der Abschluss
/// der Freizeit löscht alle Gast-Abos (<c>GastAbosBeenden</c>), spätestens der Lösch-Lauf mit den übrigen Daten der Freizeit. Abos, die der Push-Dienst nicht
/// mehr kennt (404/410), räumt die Zustellung ab.
/// </remarks>
internal sealed class GastPushAbo : IOrganisationsEigen
{
    private GastPushAbo()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid FreizeitId { get; private set; }

    /// <summary>Die Geräte-Kennung aus der Gast-Sitzung (neu bei jeder Einlösung des QR-Links); nur dieses Gerät meldet das Abo ab.</summary>
    public Guid GeraeteId { get; private set; }

    /// <summary>Der Stempel des QR-Links aus der Gast-Sitzung beim Speichern; wechselt mit jedem Erneuern des Links.</summary>
    public Guid Stempel { get; private set; }

    /// <summary>Adresse beim Push-Dienst (nur bekannte Push-Dienste, siehe <c>PushEndpunkte</c>).</summary>
    public string Endpunkt { get; private set; } = "";

    public string P256dh { get; private set; } = "";

    public string Auth { get; private set; } = "";

    public DateTimeOffset AngelegtAm { get; private set; }

    /// <summary>Nie Endpunkt, Schlüssel oder Gerät ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(GastPushAbo)}({Id})";
}
