using System.Diagnostics;
using OpenTelemetry;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Entfernt vor dem Export alles aus Spans, was Rohpfad, Abfrage oder volle URL trägt (AD-22). Übrig bleibt die
/// Routen-Vorlage <c>http.route</c>. Eine Status-Beschreibung (kann eine Ausnahme-Nachricht sein) wird geleert.
/// </summary>
public sealed class TelemetrieBereinigung : BaseProcessor<Activity>
{
    private static readonly string[] VerboteneTags =
        ["url.path", "url.query", "url.full", "http.target", "http.url", "url.original"];

    public override void OnEnd(Activity data)
    {
        foreach (var tag in VerboteneTags)
        {
            if (data.GetTagItem(tag) is not null)
            {
                data.SetTag(tag, null);
            }
        }

        if (data.StatusDescription is not null)
        {
            data.SetStatus(data.Status);
        }
    }
}

/// <summary>
/// AD-22: Die Laufzeit (z. B. <c>System.Net.Http</c> ab .NET 10) zeichnet Ausnahmen selbst als Span-Ereignis auf, an
/// <c>RecordException</c> vorbei. Ereignisse lassen sich nicht entfernen; dieser Zuhörer belegt aber vorab
/// <c>exception.message</c> und <c>exception.stacktrace</c>, sodass das Ereignis nur noch den Typ trägt.
/// </summary>
public static class AusnahmeEreignisBereinigung
{
    private static readonly Lazy<ActivityListener> Zuhoerer = new(() =>
    {
        var zuhoerer = new ActivityListener
        {
            ShouldListenTo = _ => true,
            ExceptionRecorder = (Activity _, Exception ausnahme, ref TagList tags) =>
            {
                tags.Add("exception.message", ausnahme.GetType().FullName);
                tags.Add("exception.stacktrace", "");
            },
        };
        ActivitySource.AddActivityListener(zuhoerer);
        return zuhoerer;
    });

    /// <summary>Einmal je Prozess; weitere Aufrufe sind unschädlich.</summary>
    public static void Aktivieren() => _ = Zuhoerer.Value;
}
