using Microsoft.AspNetCore.Diagnostics;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Sicherheit;
using Zeltlotse.Public;

namespace Zeltlotse.Api.Auslieferung;

/// <summary>
/// Auslieferung der beiden Clients unter derselben Adresse wie die API (AD-5):
/// <list type="bullet">
/// <item><c>/app/**</c>: die Dateien der Web-App; Pfade ohne Dateiendung fallen auf <c>/app/index.html</c> zurück.</item>
/// <item><c>/</c>: die öffentlichen Seiten (statisches Server-Rendering) und die gemeinsamen Statikdateien (<c>_content/…</c>).</item>
/// <item>Unbekannte Pfade außerhalb von <c>/api</c>: <c>404</c> als HTML-Seite „Diese Seite ist nicht verfügbar.“;
/// unter <c>/api</c> bleibt es bei ProblemDetails.</item>
/// </list>
/// </summary>
internal static class ClientAuslieferung
{
    private const string AppPfad = "/app";
    private const string AppIndex = "/app/index.html";
    private const string GemeinsamPfad = "/_content";

    /// <summary>
    /// Statuscode-Seiten: ein <c>404</c> auf GET/HEAD außerhalb von <c>/api</c> zeigt die HTML-Seite der öffentlichen Seiten;
    /// alles andere (unter <c>/api</c>, andere Status, andere Methoden) bekommt ProblemDetails.
    /// </summary>
    public static IApplicationBuilder UseStatuscodeSeiten(this IApplicationBuilder app) =>
        app.UseStatusCodePages(async kontext =>
        {
            var http = kontext.HttpContext;
            if (ApiPfad.Ist(http.Request.Path)
                || http.Response.StatusCode != StatusCodes.Status404NotFound
                || !(HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method)))
            {
                // Wie die eingebauten Statuscode-Seiten mit ProblemDetails-Dienst.
                var geschrieben = await http.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = http,
                    ProblemDetails = { Status = http.Response.StatusCode },
                });
                if (!geschrieben)
                {
                    await http.Response.WriteAsync($"Status Code: {http.Response.StatusCode}");
                }

                return;
            }

            await NichtVerfuegbarAsync(http, kontext.Next);
        });

    /// <summary>
    /// Die Web-App: Pfade unter <c>/app</c> ohne Dateiendung zeigen <c>/app/index.html</c>; <c>/app</c> leitet auf
    /// <c>/app/</c>. Die gemeinsamen Statikdateien liegen beim Host einmal unter <c>/_content</c>; die Web-App (und das
    /// Asset-Manifest ihres Service Workers) erwartet sie unter <c>/app/_content</c> und bekommt dieselben Dateien.
    /// </summary>
    public static IApplicationBuilder UseAppRueckfall(this IApplicationBuilder app) =>
        app.Use((kontext, weiter) =>
        {
            var anfrage = kontext.Request;
            if (!(HttpMethods.IsGet(anfrage.Method) || HttpMethods.IsHead(anfrage.Method))
                || !anfrage.Path.StartsWithSegments(AppPfad, StringComparison.OrdinalIgnoreCase, out var rest))
            {
                return weiter(kontext);
            }

            if (!rest.HasValue)
            {
                kontext.Response.Redirect(anfrage.PathBase + AppPfad + "/" + anfrage.QueryString, permanent: false);
                return Task.CompletedTask;
            }

            if (rest.StartsWithSegments(GemeinsamPfad, StringComparison.OrdinalIgnoreCase))
            {
                anfrage.Path = rest;
            }
            else if (!rest.Value![(rest.Value!.LastIndexOf('/') + 1)..].Contains('.', StringComparison.Ordinal))
            {
                anfrage.Path = AppIndex;
            }

            return weiter(kontext);
        });

    /// <summary>Statikdateien (Web-App, gemeinsame Bausteine) und die öffentlichen Seiten.</summary>
    public static void MapClients(this WebApplication app)
    {
        app.MapStaticAssets();
        app.MapPublicSeiten();
    }

    /// <summary>
    /// Führt den Rest der Pipeline (Routing, Endpunkte) für die Seite „nicht verfügbar“ erneut aus, mit dem ursprünglichen
    /// Status – wie <c>UseStatusCodePagesWithReExecute</c>, aber nur außerhalb von <c>/api</c>.
    /// </summary>
    private static async Task NichtVerfuegbarAsync(HttpContext http, RequestDelegate weiter)
    {
        var originalerPfad = http.Request.Path;
        var originaleAbfrage = http.Request.QueryString;
        http.Features.Set<IStatusCodeReExecuteFeature>(new StatusCodeReExecuteFeature
        {
            OriginalPath = originalerPfad.Value ?? "",
            OriginalPathBase = http.Request.PathBase.Value ?? "",
            OriginalQueryString = originaleAbfrage.HasValue ? originaleAbfrage.Value : null,
        });
        http.SetEndpoint(null);
        http.Request.RouteValues.Clear();
        http.Request.Path = PublicSeiten.NichtVerfuegbarPfad;
        http.Request.QueryString = QueryString.Empty;
        try
        {
            await weiter(http);
        }
        finally
        {
            http.Request.Path = originalerPfad;
            http.Request.QueryString = originaleAbfrage;
        }
    }
}

/// <summary>Version des Standes an jeder Antwort (AD-5), auch an Fehlern und Statikdateien.</summary>
internal static class VersionsKopfzeile
{
    public static IApplicationBuilder UseVersionsKopfzeile(this IApplicationBuilder app) =>
        app.Use((kontext, weiter) =>
        {
            kontext.Response.OnStarting(() =>
            {
                kontext.Response.Headers[Kopfzeilen.Version] = ZeltlotseVersion.Aktuell;
                return Task.CompletedTask;
            });
            return weiter(kontext);
        });
}
