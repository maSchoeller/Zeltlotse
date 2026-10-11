using System.Text.Json;

namespace Zeltlotse.Ui.Api;

/// <summary>Liest Validierungsfehler aus ProblemDetails (<c>errors</c> je JSON-Feldname, AD-27), um sie direkt am Feld zu zeigen.</summary>
internal static class ApiFehler
{
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
