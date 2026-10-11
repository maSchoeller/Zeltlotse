using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Zeltlotse.Ui.Autosave;

/// <summary>Ein Speichervorgang nach AD-19: genau ein Feld mit Version, altem und neuem Wert.</summary>
/// <param name="Pfad">Pfad der Ressource, z. B. <c>/api/freizeiten/{id}/programm/punkte/{id}</c>.</param>
/// <param name="Feldname">JSON-Name des Felds aus <c>Zeltlotse.Api.Contracts</c>.</param>
/// <param name="Version">Mitgeschickte Version der Ressource.</param>
/// <param name="AlterWert">Der Wert, den der Client zuletzt gespeichert kannte.</param>
/// <param name="NeuerWert">Der neue Wert.</param>
public sealed record AutosaveAnfrage(string Pfad, string Feldname, uint Version, string? AlterWert, string? NeuerWert);

/// <summary>Ergebnis eines Speichervorgangs.</summary>
public abstract record AutosaveErgebnis
{
    private AutosaveErgebnis()
    {
    }

    /// <summary><c>200</c>: gespeichert; neue Version und der gespeicherte Wert.</summary>
    public sealed record Gespeichert(uint Version, string? Wert) : AutosaveErgebnis;

    /// <summary><c>409</c>: derselbe Wert wurde inzwischen geändert.</summary>
    public sealed record Konflikt : AutosaveErgebnis;

    /// <summary>Netzfehler, Zeitüberschreitung, <c>5xx</c> oder eine andere unerwartete Antwort: nicht gespeichert.</summary>
    public sealed record Gescheitert : AutosaveErgebnis;
}

/// <summary>
/// Speicher-Dienst des Autosave-Bausteins (AD-19, AD-29): sendet <c>PATCH &lt;Pfad&gt;</c> mit
/// <c>{ "version": n, "alterWert": alt, "&lt;feldname&gt;": neu }</c> und wertet die Antwort aus. Er stimmt außerdem die
/// Speicher-Hinweise der Felder ab: Bekommt ein Feld den Fokus oder speichert es, verschwinden die Hinweise der anderen.
/// Nur <see cref="AutosaveFeld"/> nutzt ihn.
/// </summary>
public sealed class AutosaveDienst(HttpClient http)
{
    /// <summary>Ein Feld ist aktiv geworden (Fokus oder Speichern); die anderen Felder blenden ihren Hinweis aus.</summary>
    public event Action<object>? FeldAktiv;

    internal void Aktivieren(object feld) => FeldAktiv?.Invoke(feld);

    public async Task<AutosaveErgebnis> SpeichernAsync(AutosaveAnfrage anfrage, CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(anfrage);
        var koerper = new JsonObject
        {
            ["version"] = anfrage.Version,
            ["alterWert"] = anfrage.AlterWert,
            [anfrage.Feldname] = anfrage.NeuerWert,
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Patch, anfrage.Pfad)
            {
                Content = new StringContent(koerper.ToJsonString(), System.Text.Encoding.UTF8, "application/json"),
            };
            using var antwort = await http.SendAsync(request, abbruch);
            if (antwort.StatusCode == HttpStatusCode.Conflict)
            {
                return new AutosaveErgebnis.Konflikt();
            }

            if (!antwort.IsSuccessStatusCode)
            {
                return new AutosaveErgebnis.Gescheitert();
            }

            using var json = JsonDocument.Parse(await antwort.Content.ReadAsStringAsync(abbruch));
            var wurzel = json.RootElement;
            if (wurzel.ValueKind != JsonValueKind.Object
                || !wurzel.TryGetProperty("version", out var version)
                || !version.TryGetUInt32(out var neueVersion)
                || !wurzel.TryGetProperty(anfrage.Feldname, out var wert)
                || wert.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            {
                return new AutosaveErgebnis.Gescheitert();
            }

            return new AutosaveErgebnis.Gespeichert(neueVersion, wert.ValueKind == JsonValueKind.Null ? null : wert.GetString());
        }
        catch (HttpRequestException)
        {
            return new AutosaveErgebnis.Gescheitert();
        }
        catch (JsonException)
        {
            return new AutosaveErgebnis.Gescheitert();
        }
        catch (OperationCanceledException) when (!abbruch.IsCancellationRequested)
        {
            // Zeitüberschreitung (HttpClient-Timeout oder Zeitlimit des Verbindungs-Handlers).
            return new AutosaveErgebnis.Gescheitert();
        }
    }
}
