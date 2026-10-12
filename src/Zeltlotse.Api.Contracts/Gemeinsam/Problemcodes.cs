namespace Zeltlotse.Api.Contracts.Gemeinsam;

/// <summary>
/// Problemcodes in ProblemDetails-Antworten (RFC 9457), Erweiterung <see cref="Feld"/>. Jede Fehlerantwort trägt zusätzlich <c>traceId</c>.
/// Ändert sich nur mit einer Änderung der Architektur-Spine (AD-5).
/// </summary>
public static class Problemcodes
{
    /// <summary>Name der ProblemDetails-Erweiterung, die den Code trägt.</summary>
    public const string Feld = "code";

    /// <summary><c>400</c>: Eingaben ungültig; <c>errors</c> enthält die Fehler je JSON-Feldname.</summary>
    public const string Validierung = "validierung";

    /// <summary><c>400</c>: Ändernde Anfrage ohne gültiges Antiforgery-Token und ohne gültige Dienst-Kennung (AD-25).</summary>
    public const string AntiforgeryUngueltig = "antiforgery-ungueltig";

    /// <summary><c>409</c>: Der Wert desselben Felds hat sich seit der mitgeschickten Version geändert (AD-19).</summary>
    public const string Konflikt = "konflikt";

    /// <summary><c>409</c>: Die Freizeit ist abgeschlossen („gelaufen“) und nur noch lesbar (AD-17, FR-45).</summary>
    public const string FreizeitAbgeschlossen = "freizeit-abgeschlossen";

    /// <summary><c>429</c>: Zu viele Anfragen; <c>Retry-After</c> nennt die Wartezeit in Sekunden.</summary>
    public const string Zugriffsbegrenzt = "zugriffsbegrenzt";

    /// <summary><c>500</c>: Unerwarteter Fehler; die Antwort nennt nur den Ausnahmetyp und die Trace-ID.</summary>
    public const string UnerwarteterFehler = "unerwarteter-fehler";
}
