using System.Net;

namespace Zeltlotse.Ui.Transport;

/// <summary>Empfänger der Meldung „Sitzung fehlt“ des <see cref="SitzungsHandler"/>; umgesetzt von der Web-App (AD-12).</summary>
public interface ISitzung
{
    /// <summary>Eine API-Antwort war <c>401</c>: Die Sitzung ist abgelaufen, abgemeldet oder anderswo beendet.</summary>
    ValueTask AbgelaufenAsync();
}

/// <summary>
/// Meldet jede <c>401</c>-Antwort der API zentral an <see cref="ISitzung"/>, damit nicht jede Seite selbst zur Anmeldung
/// weiterleitet. Ausgenommen ist <c>POST /api/konten/anmelden</c>: Dort heißt <c>401</c> „Mail-Adresse oder Passwort stimmt
/// nicht“ und die Seite zeigt den Fehler selbst. Die Antwort geht unverändert an den Aufrufer.
/// </summary>
public sealed class SitzungsHandler(ISitzung sitzung) : DelegatingHandler
{
    /// <summary>Pfad, dessen <c>401</c> keine fehlende Sitzung bedeutet.</summary>
    public const string PasswortAnmeldung = "/api/konten/anmelden";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var antwort = await base.SendAsync(request, cancellationToken);
        if (antwort.StatusCode == HttpStatusCode.Unauthorized
            && !string.Equals(request.RequestUri?.AbsolutePath, PasswortAnmeldung, StringComparison.OrdinalIgnoreCase))
        {
            await sitzung.AbgelaufenAsync();
        }

        return antwort;
    }
}
