using Zeltlotse.Freizeiten.Contracts.Rollen;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Api.Rechte;

/// <summary>
/// Zentrale Rechteprüfung (AD-6, AD-12) nach Routing und Authentifizierung, vor der Modellbindung: Sie setzt den
/// Mandanten-Kontext aus der adressierten Ressource und prüft die Endpunkt-Merkmale.
/// <list type="bullet">
/// <item><c>.NurGlobalAdmin()</c>: kein Global Admin → <c>403</c>.</item>
/// <item>Routenwert <c>orgId</c>: Mandant = diese Organisation. <c>.NurOrgRolle(…)</c>: keine Rolle in dieser Organisation
/// (auch unbekannte Organisation, auch Global Admin ohne eigene Rolle) → <c>404</c>; Rolle vorhanden, aber nicht die
/// verlangte → <c>403</c>.</item>
/// <item><c>.NurFreizeitRolle(…)</c>: Mandant = Organisation der Freizeit aus dem Routenwert <c>freizeitId</c>; unbekannte
/// Freizeit oder keine eigene Freizeit-Rolle (auch Org-Admin, Org-Überblick, Global Admin) → <c>404</c>; Rolle vorhanden,
/// aber nicht die verlangte → <c>403</c>.</item>
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
        var abbruch = kontext.RequestAborted;
        var orgMerkmal = endpunkt?.Metadata.GetMetadata<NurOrgRolleMetadaten>();
        var freizeitMerkmal = endpunkt?.Metadata.GetMetadata<NurFreizeitRolleMetadaten>();
        if (orgMerkmal is not null && freizeitMerkmal is not null)
        {
            throw new InvalidOperationException($"{endpunkt!.DisplayName}: .NurOrgRolle() und .NurFreizeitRolle() an demselben Endpunkt sind verboten.");
        }

        var organisationId = Kennung(kontext, Rechtemerkmale.OrgRouteWert);
        if (organisationId is not null)
        {
            mandant.Setzen(organisationId);
        }

        if (endpunkt?.Metadata.GetMetadata<NurGlobalAdminMetadaten>() is not null && !await Rollen().IstGlobalAdminAsync(abbruch))
        {
            kontext.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (orgMerkmal is not null)
        {
            var orgId = organisationId
                        ?? throw new InvalidOperationException($"{endpunkt!.DisplayName}: .NurOrgRolle() verlangt den Routenwert {{{Rechtemerkmale.OrgRouteWert}:guid}}.");
            if (Status(await Rollen().OrgRollenAsync(orgId, abbruch), orgMerkmal.Rollen) is { } status)
            {
                kontext.Response.StatusCode = status;
                return;
            }
        }

        if (freizeitMerkmal is not null)
        {
            var freizeitId = Kennung(kontext, Rechtemerkmale.FreizeitRouteWert)
                             ?? throw new InvalidOperationException($"{endpunkt!.DisplayName}: .NurFreizeitRolle() verlangt den Routenwert {{{Rechtemerkmale.FreizeitRouteWert}:guid}}.");
            var freizeitOrganisation = await kontext.RequestServices.GetRequiredService<IFreizeitRollenLesen>().OrganisationAsync(freizeitId, abbruch);
            if (freizeitOrganisation is null)
            {
                kontext.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            mandant.Setzen(freizeitOrganisation);
            if (Status(await Rollen().FreizeitRollenAsync(freizeitId, abbruch), freizeitMerkmal.Rollen) is { } status)
            {
                kontext.Response.StatusCode = status;
                return;
            }
        }

        await weiter(kontext);
    }

    private static Guid? Kennung(HttpContext kontext, string routenWert) =>
        kontext.Request.RouteValues.TryGetValue(routenWert, out var wert) && Guid.TryParse(wert?.ToString(), out var id) ? id : null;

    /// <summary><c>404</c> ohne eigene Rolle, <c>403</c> mit Rolle, aber nicht der verlangten; sonst <c>null</c>.</summary>
    private static int? Status<TRolle>(IReadOnlyCollection<TRolle> eigene, IReadOnlyList<TRolle> verlangt)
    {
        if (eigene.Count == 0)
        {
            return StatusCodes.Status404NotFound;
        }

        return eigene.Any(verlangt.Contains) ? null : StatusCodes.Status403Forbidden;
    }
}
