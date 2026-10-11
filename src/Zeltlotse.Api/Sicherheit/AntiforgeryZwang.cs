using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Features;
using Zeltlotse.Api.Contracts.Gemeinsam;

namespace Zeltlotse.Api.Sicherheit;

/// <summary>
/// Zentraler Schutz gegen gefälschte Anfragen (AD-25): Jede Anfrage mit POST, PUT, PATCH oder DELETE unter <c>/api</c>
/// braucht ein gültiges Antiforgery-Token im Header <c>X-Zeltlotse-Antiforgery</c> (Cookie <c>__Host-Zeltlotse-Antiforgery</c>).
/// Einzige Ausnahme: die gültige Dienst-Kennung der öffentlichen Seiten. Eine vorhandene, aber falsche Dienst-Kennung
/// fällt nicht auf das Token zurück.
/// </summary>
internal sealed class AntiforgeryZwang(RequestDelegate weiter)
{
    public const string CookieName = "__Host-Zeltlotse-Antiforgery";

    public async Task InvokeAsync(
        HttpContext kontext,
        IAntiforgery antiforgery,
        PublicSeitenDienstKennung dienstKennung,
        IProblemDetailsService problemDetails)
    {
        // Wie die eingebaute Middleware: Endpunkte mit Antiforgery-Metadaten (Formular-Bindung) verlangen diesen Eintrag.
        kontext.Items[MiddlewareAufgerufen] = true;

        var verlangt = kontext.GetEndpoint()?.Metadata.GetMetadata<IAntiforgeryMetadata>() is { RequiresValidation: true };
        if (!IstAendernd(kontext.Request.Method) || !(ApiPfad.Ist(kontext.Request.Path) || verlangt))
        {
            await weiter(kontext);
            return;
        }

        // Nur der Header zählt: ohne ihn kein Rückfall auf ein Formularfeld (und kein Puffern großer anonymer Körper).
        var gueltig = kontext.Request.Headers.TryGetValue(Kopfzeilen.Dienst, out var dienst)
            ? dienstKennung.Pruefen(dienst.ToString())
            : !string.IsNullOrEmpty(kontext.Request.Headers[Kopfzeilen.Antiforgery])
              && await antiforgery.IsRequestValidAsync(kontext);

        if (!gueltig)
        {
            kontext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await problemDetails.WriteAsync(new ProblemDetailsContext
            {
                HttpContext = kontext,
                ProblemDetails =
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Die Anfrage ist nicht gegen Fälschung geschützt.",
                    Extensions = { [Problemcodes.Feld] = Problemcodes.AntiforgeryUngueltig },
                },
            });
            return;
        }

        kontext.Features.Set<IAntiforgeryValidationFeature>(new Geprueft());
        await weiter(kontext);
    }

    /// <summary>Schlüssel, den die Endpunkt-Middleware von ASP.NET Core für Endpunkte mit Antiforgery-Metadaten prüft.</summary>
    internal const string MiddlewareAufgerufen = "__AntiforgeryMiddlewareWithEndpointInvoked";

    private sealed class Geprueft : IAntiforgeryValidationFeature
    {
        public bool IsValid => true;

        public Exception? Error => null;
    }

    public static bool IstAendernd(string methode) =>
        HttpMethods.IsPost(methode) || HttpMethods.IsPut(methode) || HttpMethods.IsPatch(methode) || HttpMethods.IsDelete(methode);
}
