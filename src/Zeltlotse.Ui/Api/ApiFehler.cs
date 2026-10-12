using System.Text.Json;

namespace Zeltlotse.Ui.Api;

/// <summary>Liest Validierungsfehler aus ProblemDetails (<c>errors</c> je JSON-Feldname, AD-27), um sie direkt am Feld zu zeigen.</summary>
internal static class ApiFehler
{
    /// <summary>Je Feld die erste Meldung; leer, wenn die Antwort keine Feldfehler trägt.</summary>
    public static async Task<IReadOnlyDictionary<string, string>> AlleFeldFehlerAsync(HttpResponseMessage antwort, CancellationToken abbruch)
    {
        var ergebnis = new Dictionary<string, string>(StringComparer.Ordinal);
        if (antwort.Content.Headers.ContentType?.MediaType != "application/problem+json")
        {
            return ergebnis;
        }

        try
        {
            using var problem = JsonDocument.Parse(await antwort.Content.ReadAsStringAsync(abbruch));
            if (problem.RootElement.ValueKind == JsonValueKind.Object
                && problem.RootElement.TryGetProperty("errors", out var fehler)
                && fehler.ValueKind == JsonValueKind.Object)
            {
                foreach (var feld in fehler.EnumerateObject())
                {
                    if (feld.Value.ValueKind == JsonValueKind.Array && feld.Value.GetArrayLength() > 0 && feld.Value[0].GetString() is { } text)
                    {
                        ergebnis[feld.Name] = text;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Kein lesbarer Körper: keine Feldfehler.
        }

        return ergebnis;
    }

    /// <summary>Die erste Meldung zum Feld oder <c>null</c>, wenn die Antwort keinen Fehler für dieses Feld trägt.</summary>
    public static async Task<string?> FeldFehlerAsync(HttpResponseMessage antwort, string feld, CancellationToken abbruch)
    {
        if (antwort.Content.Headers.ContentType?.MediaType != "application/problem+json")
        {
            return null;
        }

        try
        {
            using var problem = JsonDocument.Parse(await antwort.Content.ReadAsStringAsync(abbruch));
            return problem.RootElement.ValueKind == JsonValueKind.Object
                   && problem.RootElement.TryGetProperty("errors", out var fehler)
                   && fehler.ValueKind == JsonValueKind.Object
                   && fehler.TryGetProperty(feld, out var meldungen)
                   && meldungen.ValueKind == JsonValueKind.Array
                   && meldungen.GetArrayLength() > 0
                ? meldungen[0].GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
