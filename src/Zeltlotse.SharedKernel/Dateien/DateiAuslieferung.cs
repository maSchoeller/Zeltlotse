using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Zeltlotse.SharedKernel.Dateien;

/// <summary>
/// Liefert eine Datei über den Endpunkt des besitzenden Moduls aus (AD-15): Bilder <c>inline</c>, PDF <c>attachment</c>,
/// Content-Type aus der Prüfung des Inhalts. Öffentliche oder vorsignierte URLs gibt es nicht.
/// </summary>
public static class DateiAuslieferung
{
    /// <exception cref="InvalidOperationException">Der Inhalt ist kein erlaubter Typ.</exception>
    public static IResult Ausliefern(byte[] inhalt, string dateiname)
    {
        ArgumentNullException.ThrowIfNull(inhalt);
        var art = DateiPruefung.ArtErkennen(inhalt)
                  ?? throw new InvalidOperationException("Der Inhalt ist kein erlaubter Dateityp und wird nicht ausgeliefert.");
        return new DateiErgebnis(inhalt, art, dateiname);
    }

    private sealed class DateiErgebnis(byte[] inhalt, DateiArt art, string dateiname) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            var disposition = new ContentDispositionHeaderValue(DateiPruefung.IstBild(art) ? "inline" : "attachment");
            disposition.SetHttpFileName(string.IsNullOrWhiteSpace(dateiname) ? "datei" : Path.GetFileName(dateiname));

            var antwort = httpContext.Response;
            antwort.StatusCode = StatusCodes.Status200OK;
            antwort.ContentType = DateiPruefung.ContentType(art);
            antwort.ContentLength = inhalt.Length;
            antwort.Headers.ContentDisposition = disposition.ToString();
            antwort.Headers.XContentTypeOptions = "nosniff";
            await antwort.Body.WriteAsync(inhalt, httpContext.RequestAborted);
        }
    }
}
