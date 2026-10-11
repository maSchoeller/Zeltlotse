using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Api.Rechte;

/// <summary>
/// Zentrale Rechteprüfung (AD-6, AD-12) nach Routing und Authentifizierung, vor der Modellbindung: Sie setzt den
/// Mandanten-Kontext aus dem Routenwert <c>orgId</c> und prüft die Endpunkt-Merkmale.
/// <list type="bullet">
/// <item><c>.NurGlobalAdmin()</c>: kein Global Admin → <c>403</c>.</item>
/// <item><c>.NurOrgRolle(…)</c>: keine Rolle in dieser Organisation (auch unbekannte Organisation, auch Global Admin ohne
/// eigene Rolle) → <c>404</c>; Rolle vorhanden, aber nicht die verlangte → <c>403</c>.</item>
/// </list>
/// Den Körper (ProblemDetails) schreiben die Statuscode-Seiten. Kein Modul prüft Rollen selbst.
/// </summary>
internal sealed class RechtePruefung(RequestDelegate weiter)
{
    public async Task InvokeAsync(HttpContext kontext, MandantenKontext mandant)
    {
        // Die Rollen erst bei Bedarf: Statikdateien und Endpunkte ohne Merkmal kosten keine Abfrage.
        IKontextRollen Rollen() => kontext.RequestServices.GetRequiredService<IKontextRollen>();

        var endpunkt = kontext.GetEndpoint();
        Guid? organisationId = kontext.Request.RouteValues.TryGetValue(Rechtemerkmale.OrgRouteWert, out var wert)
                               && Guid.TryParse(wert?.ToString(), out var id)
            ? id
            : null;
        if (organisationId is not null)
        {
            mandant.Setzen(organisationId);
        }

        var abbruch = kontext.RequestAborted;
        if (endpunkt?.Metadata.GetMetadata<NurGlobalAdminMetadaten>() is not null && !await Rollen().IstGlobalAdminAsync(abbruch))
        {
            kontext.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (endpunkt?.Metadata.GetMetadata<NurOrgRolleMetadaten>() is { } verlangt)
        {
            var orgId = organisationId
                        ?? throw new InvalidOperationException($"{endpunkt.DisplayName}: .NurOrgRolle() verlangt den Routenwert {{{Rechtemerkmale.OrgRouteWert}:guid}}.");
            var eigene = await Rollen().OrgRollenAsync(orgId, abbruch);
            if (eigene.Count == 0)
            {
                kontext.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (!eigene.Any(verlangt.Rollen.Contains))
            {
                kontext.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }

        await weiter(kontext);
    }
}
