namespace Zeltlotse.Api.Sicherheit;

/// <summary>
/// Header jeder Antwort (AD-25), auch für Fehler und <c>/health</c>: gesetzt kurz vor dem Senden, damit auch
/// Antworten der Ausnahme-Behandlung, der Status-Seiten und der Zugriffsbegrenzung sie tragen.
/// Unter <c>/api</c> zusätzlich <c>Cache-Control: no-store</c> (AD-18).
/// </summary>
internal static class SicherheitsHeader
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    public static IApplicationBuilder UseSicherheitsHeader(this IApplicationBuilder app) =>
        app.Use((kontext, weiter) =>
        {
            kontext.Response.OnStarting(() =>
            {
                var header = kontext.Response.Headers;
                header.ContentSecurityPolicy = ContentSecurityPolicy;
                header["Referrer-Policy"] = "no-referrer";
                header.XContentTypeOptions = "nosniff";
                if (ApiPfad.Ist(kontext.Request.Path))
                {
                    header.CacheControl = "no-store";
                }

                return Task.CompletedTask;
            });
            return weiter(kontext);
        });
}

/// <summary>Gemeinsame Pfadregel: <c>/api</c> und alles darunter.</summary>
internal static class ApiPfad
{
    public static bool Ist(PathString pfad) => pfad.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);
}
