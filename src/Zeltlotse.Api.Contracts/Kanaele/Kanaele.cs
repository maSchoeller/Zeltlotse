using System.Text.Json.Serialization;

namespace Zeltlotse.Api.Contracts.Kanaele;

/// <summary>
/// Story 6-1 (CAP-35, FR-61): die zwei festen Kanäle jeder Freizeit. In der Route steht der Name klein (<c>team</c>, <c>alle</c>), im JSON der Name.
/// Werte sind gespeichert; nur anhängen.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<Kanal>))]
public enum Kanal
{
    /// <summary>Leitung und Team.</summary>
    Team = 1,

    /// <summary>Alle Personen der Freizeit; eine Nachricht geht erst nach ausdrücklicher Bestätigung hinaus.</summary>
    Alle = 2,
}

/// <summary>
/// Story 6-1: Antwort von <c>GET /api/freizeiten/{freizeitId}/kanaele/{kanal}/nachrichten</c> (Leitung und Team): höchstens die neuesten 200 Nachrichten
/// des Kanals, älteste zuerst (die neueste steht unten).
/// </summary>
/// <param name="Kanal">Der gelesene Kanal.</param>
/// <param name="Nachrichten">Die Nachrichten, älteste zuerst.</param>
/// <param name="PersonenAlle">Wie viele Personen der Kanal „Alle“ erreicht (bestätigte Teilnehmende sowie Leitung und Team); das N in „An N Personen senden?“.</param>
public sealed record KanalAnsicht(Kanal Kanal, IReadOnlyList<KanalNachricht> Nachrichten, int PersonenAlle);

/// <summary>Story 6-1: Eine Nachricht in einem Kanal, in <see cref="KanalAnsicht"/> und als Antwort auf das Senden (<c>201</c>).</summary>
/// <param name="AutorPersonId">
/// Wer geschrieben hat (AD-7). Den Namen setzt der Client aus Freizeit und Personenliste zusammen (AD-2); nach dem Löschen der Person steht hier
/// <see cref="KanalFelder.GeloeschtePerson"/>.
/// </param>
/// <param name="GesendetAm">Zeitpunkt des Sendens (Uhr des Servers).</param>
public sealed record KanalNachricht(Guid Id, Guid AutorPersonId, string Text, DateTimeOffset GesendetAm);

/// <summary>
/// Story 6-1: Körper von <c>POST /api/freizeiten/{freizeitId}/kanaele/{kanal}/nachrichten</c>. Im Kanal „Alle“ muss <see cref="Bestaetigt"/>
/// <c>true</c> sein (AD-13), sonst <c>400</c> am Feld <c>bestaetigt</c>.
/// </summary>
/// <param name="Text">1 bis 1000 Zeichen, ohne Leerzeichen am Anfang und Ende.</param>
/// <param name="Bestaetigt">Die Inline-Bestätigung „An N Personen senden?“ wurde mit „Ja, senden“ beantwortet.</param>
public sealed record NachrichtNeu(string? Text, bool? Bestaetigt = null);

/// <summary>
/// Story 6-1: Körper von <c>POST /api/freizeiten/{freizeitId}/kanaele/push/abo</c>: das Push-Abo des Browsers (Web-Push-Standard, AD-21). Je Freizeit
/// und Endpunkt gibt es ein Abo; erneutes Senden überschreibt es.
/// </summary>
/// <param name="Endpunkt">Adresse beim Push-Dienst des Browsers: nur <c>https</c> zu einem bekannten Push-Dienst.</param>
/// <param name="P256dh">Öffentlicher Schlüssel des Browsers (base64url, 65 Bytes).</param>
/// <param name="Auth">Authentifizierungsgeheimnis des Browsers (base64url, 16 Bytes).</param>
public sealed record PushAboNeu(string? Endpunkt, string? P256dh, string? Auth);

/// <summary>Story 6-1: Körper von <c>POST /api/freizeiten/{freizeitId}/kanaele/push/abo/abmelden</c>: löscht das eigene Abo zu diesem Endpunkt.</summary>
public sealed record PushAbmelden(string? Endpunkt);

/// <summary>Story 6-1: Antwort von <c>GET /api/freizeiten/{freizeitId}/kanaele/push/schluessel</c>: der öffentliche VAPID-Schlüssel (base64url).</summary>
public sealed record PushSchluessel(string OeffentlicherSchluessel);

/// <summary>Story 6-1: JSON-Namen der Felder für Feldfehler, Grenzen und der Platzhalter für gelöschte Personen.</summary>
public static class KanalFelder
{
    public const string Text = "text";

    public const string Bestaetigt = "bestaetigt";

    public const string Endpunkt = "endpunkt";

    public const string P256dh = "p256dh";

    public const string Auth = "auth";

    public const int TextMaximaleLaenge = 1000;

    /// <summary>Höchstens so viele Nachrichten liefert die Liste.</summary>
    public const int HoechstzahlNachrichten = 200;

    /// <summary>
    /// Platzhalter für eine gelöschte Person in <see cref="KanalNachricht.AutorPersonId"/> (AD-16): dieselbe feste Kennung wie im Server; zu ihr gibt es nie
    /// ein Profil. Der Client zeigt dafür „Gelöschte Person“.
    /// </summary>
    public static readonly Guid GeloeschtePerson = Guid.Parse("00000000-0000-7000-8000-0000000de1e7");

    /// <summary>Der Kanal aus der Route (<c>team</c> oder <c>alle</c>, ohne Rücksicht auf Groß- und Kleinschreibung); sonst <c>null</c>.</summary>
    public static Kanal? AusRoute(string? wert) => wert?.ToLowerInvariant() switch
    {
        "team" => Kanal.Team,
        "alle" => Kanal.Alle,
        _ => null,
    };

    /// <summary>Der Kanal in der Route.</summary>
    public static string InRoute(Kanal kanal) => kanal == Kanal.Alle ? "alle" : "team";
}
