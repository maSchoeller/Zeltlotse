using System.Globalization;
using Zeltlotse.Api.Contracts.Kanaele;
using Zeltlotse.Ui.Komponenten;

namespace Zeltlotse.App.Features.Teilnehmer;

/// <summary>Story 6-4: Texte und Ziele des Teilnehmer-Zugangs per QR-Code (EXPERIENCE.md → „Teilnehmer-Zugang beendet“, NS-10).</summary>
public static class TeilnehmerTexte
{
    /// <summary>Überschrift der Seite, die der QR-Code öffnet.</summary>
    public const string Ueberschrift = "Teilnehmer-Zugang";

    /// <summary>Erklärung vor dem Einlösen.</summary>
    public const string Erklaerung = "Hier stehen die Wochenübersicht, die Infos und die Nachrichten an alle. Ein Konto braucht es nicht.";

    /// <summary>EXPERIENCE.md: Jede Antwort 404 heißt für den Gast, dass der Zugang nicht (mehr) gilt.</summary>
    public const string EndeText = "Diese Freizeit ist zu Ende.";

    /// <summary>Zusatz zu <see cref="EndeText"/>: Ein erneuerter QR-Code macht den alten ungültig; sonst hat die Leitung das Modul abgeschaltet.</summary>
    public const string EndeZusatz = "Wurde der QR-Code erneuert, hilft ein neuer Scan am Infobrett. Sonst hat die Leitung den Zugang ausgeschaltet.";

    /// <summary>Antwort 429 beim Einlösen: Die Zugriffsbegrenzung je IP greift, wenn im Lager-WLAN viele gleichzeitig scannen.</summary>
    public const string ZuVieleText = "Gerade wollen viele gleichzeitig hinein. Bitte in einer Minute noch einmal tippen.";

    /// <summary>Name des Absenders aller Nachrichten im Kanal „Alle“ (der Gast sieht keine Namen aus dem Team).</summary>
    public const string Absender = "Team";

    public const string KeineInfosText = "Noch keine Infos.";

    public const string KeinProgrammText = "An diesem Tag ist noch nichts geplant.";

    /// <summary>Push möglich, aber aus.</summary>
    public const string PushAusText = "Neue Nachrichten an alle kommen dann als Benachrichtigung auf dieses Gerät.";

    /// <summary>Unter jedem Hinweis ohne Push.</summary>
    public const string NachrichtenBleibenText = "Die Nachrichten bleiben hier lesbar.";

    /// <summary>Die Startseite des Teilnehmer-Zugangs (relativ zur Basis <c>/app/</c>).</summary>
    public const string Start = "teilnehmer";

    /// <summary>Die drei Bereiche der Startseite: Woche (ohne Zusatz), Infos und Alle.</summary>
    public static string Ziel(string bereich) => bereich == "woche" ? Start : $"{Start}?bereich={bereich}";

    /// <summary>Ein Tag der Wochenübersicht am Handy.</summary>
    public static string TagZiel(DateOnly tag) => $"{Start}?tag={tag.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

    /// <summary>Die Reiter „Woche / Infos / Alle“.</summary>
    public static IReadOnlyList<ReiterEintrag> Bereiche(string aktiv) =>
    [
        new("Woche", Ziel("woche"), aktiv == "woche"),
        new("Infos", Ziel("infos"), aktiv == "infos"),
        new("Alle", Ziel("alle"), aktiv == "alle"),
    ];

    /// <summary>Der Bereich aus der Adresse; unbekannt oder leer heißt „woche“.</summary>
    public static string Bereich(string? wert) => wert?.ToLowerInvariant() switch
    {
        "infos" => "infos",
        "alle" => "alle",
        _ => "woche",
    };

    /// <summary>Eine Nachricht für die Liste: immer „Team“ als Absender, ohne Autor.</summary>
    public static KanalNachrichtEintrag Eintrag(TeilnehmerNachricht nachricht) => new("T", Absender, nachricht.GesendetAm, nachricht.Text);
}
