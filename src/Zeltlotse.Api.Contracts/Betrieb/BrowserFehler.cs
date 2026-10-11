namespace Zeltlotse.Api.Contracts.Betrieb;

/// <summary>
/// Körper von <c>POST /api/betrieb/fehler</c>: ein unerwarteter Fehler im Browser, nur mit Fehlerart, Seite und Trace-ID
/// (Konvention „Logging“). Die Seite wird vor dem Loggen bereinigt (ohne Abfrage, Fragment und Token-Segmente).
/// </summary>
/// <param name="Fehlerart">Art des Fehlers, z. B. <c>TypeError</c>: nur Buchstaben, Ziffern und <c>._:-</c>, höchstens 100 Zeichen.</param>
/// <param name="Seite">Pfad der Seite, beginnt mit <c>/</c>, höchstens 2000 Zeichen.</param>
/// <param name="TraceId">Trace-ID der letzten Antwort: Buchstaben, Ziffern und <c>-</c>, höchstens 100 Zeichen.</param>
public sealed record BrowserFehler(string? Fehlerart, string? Seite, string? TraceId);
