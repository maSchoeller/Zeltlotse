using OpenTelemetry;
using OpenTelemetry.Logs;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Entfernt vor dem Export die Ausnahme aus jedem Log-Eintrag (AD-22): Framework-Logger (EF Core, Npgsql, Hosting)
/// hängen sie mit Nachricht und Stacktrace an, und Datenbank-Meldungen können Werte enthalten.
/// Übrig bleibt nur der Typname als Attribut <c>exception.type</c>.
/// </summary>
public sealed class LogBereinigung : BaseProcessor<LogRecord>
{
    public const string TypAttribut = "exception.type";

    public override void OnEnd(LogRecord data)
    {
        if (data.Exception is not { } ausnahme)
        {
            return;
        }

        data.Attributes = [.. data.Attributes ?? [], new KeyValuePair<string, object?>(TypAttribut, ausnahme.GetType().FullName)];
        data.Exception = null;
    }
}
