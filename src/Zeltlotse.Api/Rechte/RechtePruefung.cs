using Microsoft.AspNetCore.Authentication;
using Zeltlotse.Freizeiten.Contracts.Rollen;
using Zeltlotse.Freizeiten.Contracts.TeilnehmerZugang;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Api.Rechte;

/// <summary>
/// Zentrale Rechteprüfung (AD-6, AD-12) nach Routing und Authentifizierung, vor der Modellbindung: Sie setzt den
/// Mandanten-Kontext aus der adressierten Ressource und prüft die Endpunkt-Merkmale.
/// <list type="bullet">
/// <item><c>.ErneuteAnmeldung()</c>: Anmeldung fehlt oder liegt länger als <see cref="Sitzung.FrischeDauer"/> zurück →
/// <c>401</c> (AD-25).</item>
/// <item><c>.NurGlobalAdmin()</c>: kein Global Admin → <c>403</c>.</item>
/// <item>Routenwert <c>orgId</c>: Mandant = diese Organisation. <c>.NurOrgRolle(…)</c>: keine Rolle in dieser Organisation
/// (auch unbekannte Organisation, auch Global Admin ohne eigene Rolle) → <c>404</c>; Rolle vorhanden, aber nicht die
/// verlangte → <c>403</c>.</item>
/// <item><c>.NurFreizeitRolle(…)</c>: Mandant = Organisation der Freizeit aus dem Routenwert <c>freizeitId</c>; unbekannte
/// Freizeit oder keine eigene Freizeit-Rolle (auch Org-Admin, Org-Überblick, Global Admin) → <c>404</c>; Rolle vorhanden,
/// aber nicht die verlangte → <c>403</c>.</item>
/// <item>Story 3-7: <c>.GehoertZuSchalter(…)</c> (nur mit <c>.NurFreizeitRolle(…)</c>): nach bestandener Rollenprüfung, Schalter für die
/// Freizeit aus → <c>404</c> (AD-20).</item>
/// <item>Story 6-4: <c>.NurQrGast()</c> (immer allein): Gast-Sitzung fehlt oder gilt nicht (mehr) → <c>404</c>, nie <c>401</c>; sonst Mandant = Organisation
/// der Freizeit des QR-Links und <see cref="Zeltlotse.SharedKernel.Rechte.IQrGast"/> gesetzt.</item>
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

        var schalterMerkmal = endpunkt?.Metadata.GetMetadata<GehoertZuSchalterMetadaten>();
        if (schalterMerkmal is not null && freizeitMerkmal is null)
        {
            throw new InvalidOperationException($"{endpunkt!.DisplayName}: .GehoertZuSchalter() verlangt .NurFreizeitRolle() an demselben Endpunkt.");
        }

        // Story 6-4: Der QR-Gast steht immer allein; jedes weitere Rechtemerkmal wäre eine zweite, widersprüchliche Regel.
        if (endpunkt?.Metadata.GetMetadata<NurQrGastMetadaten>() is not null)
        {
            if (orgMerkmal is not null || freizeitMerkmal is not null || schalterMerkmal is not null
                || endpunkt.Metadata.GetMetadata<NurGlobalAdminMetadaten>() is not null
                || endpunkt.Metadata.GetMetadata<ErneuteAnmeldungMetadaten>() is not null
                || endpunkt.Metadata.GetMetadata<Zeltlotse.SharedKernel.Web.OrganisationsuebergreifendMetadaten>() is not null)
            {
                throw new InvalidOperationException($"{endpunkt.DisplayName}: .NurQrGast() verträgt kein weiteres Rechtemerkmal an demselben Endpunkt.");
            }

            if (!await QrGastPruefenAsync(kontext, mandant, abbruch))
            {
                kontext.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await weiter(kontext);
            return;
        }

        var organisationId = Kennung(kontext, Rechtemerkmale.OrgRouteWert);
        if (organisationId is not null)
        {
            mandant.Setzen(organisationId);
        }

        if (endpunkt?.Metadata.GetMetadata<ErneuteAnmeldungMetadaten>() is not null && !AnmeldungFrisch(kontext))
        {
            kontext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
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

            // AD-20: erst nach bestandener Rollenprüfung, damit die Antwort nichts über Module verrät, die die Person nicht sieht.
            if (schalterMerkmal is not null
                && !await kontext.RequestServices.GetRequiredService<ISchalterStand>().IstAnAsync(freizeitId, schalterMerkmal.Schalter, abbruch))
            {
                kontext.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
        }

        await weiter(kontext);
    }

    /// <summary>
    /// Story 6-4 (AD-12, AD-20): Authentifiziert das Gast-Schema (nie das Vorgabe-Schema) und prüft bei Freizeiten, ob der QR-Link mit diesem Stempel noch
    /// besteht, der Teilnehmer-Zugang an ist und das Freizeitende noch nicht erreicht ist. Dann Mandant = Organisation der Freizeit und der QR-Gast gesetzt.
    /// </summary>
    private static async Task<bool> QrGastPruefenAsync(HttpContext kontext, MandantenKontext mandant, CancellationToken abbruch)
    {
        var ergebnis = await kontext.AuthenticateAsync(GastSitzung.Schema);
        if (!ergebnis.Succeeded || GastSitzung.Lesen(ergebnis.Principal) is not { } gast)
        {
            return false;
        }

        var organisation = await kontext.RequestServices.GetRequiredService<IQrZugangLesen>().PruefenAsync(gast.FreizeitId, gast.Stempel, abbruch);
        if (organisation is null)
        {
            return false;
        }

        mandant.Setzen(organisation);
        kontext.RequestServices.GetRequiredService<QrGastKontext>().Setzen(gast.FreizeitId, gast.GeraeteId, gast.Stempel);
        return true;
    }

    /// <summary>AD-25: Die Anmeldung der Sitzung liegt höchstens <see cref="Sitzung.FrischeDauer"/> zurück (Uhr über <see cref="TimeProvider"/>).</summary>
    private static bool AnmeldungFrisch(HttpContext kontext)
    {
        var angemeldetAm = Sitzung.AngemeldetAm(kontext.User);
        var jetzt = kontext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        return angemeldetAm is { } am && jetzt - am <= Sitzung.FrischeDauer;
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
