using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Betrieb;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Dateien;

namespace Zeltlotse.Api.Tests;

/// <summary>Endpunkte nur für Tests; der Host bildet sie nur in der Umgebung <c>Testing</c> ab.</summary>
internal sealed class TestEndpunkte : IZusatzEndpunkte
{
    public const string Name = "Erika Muster";

    public void Abbilden(IEndpointRouteBuilder api)
    {
        api.MapGet("/test/ausnahme", string () => throw new InvalidOperationException($"{Name} hat Erdnussallergie"));
        api.MapPost("/test/validierung", string () => throw new ValidierungsAusnahme(
            new Dictionary<string, string[]> { ["vorname"] = ["Pflichtfeld."], ["adresse.plz"] = ["Fünf Ziffern."] }));
        api.MapPost("/test/aendern", () => TypedResults.NoContent());
        api.MapGet("/test/ok", () => TypedResults.Ok(new { ok = true }));
        api.MapGet("/test/keks", (HttpContext kontext) =>
        {
            // Absichtlich lasche Optionen: die zentrale Cookie-Richtlinie muss sie verschärfen.
            kontext.Response.Cookies.Append("__Host-Test", "1", new CookieOptions { Secure = false, HttpOnly = false, SameSite = SameSiteMode.None, Path = "/api" });
            return TypedResults.NoContent();
        });
        api.MapPost("/test/upload", async (IFormFile datei) =>
            TypedResults.Ok(new { art = (await DateiPruefung.LesenUndPruefenAsync(datei)).Art?.ToString() }));
        api.MapGet("/test/ausnahme-mit-token/{token}", string (string token) => throw new InvalidOperationException("mit Token"));
        api.MapGet("/test/keks-ohne-praefix", (HttpContext kontext) =>
        {
            kontext.Response.Cookies.Append("zeltlotse", "1");
            return TypedResults.NoContent();
        });
    }

    /// <summary>Ein Formular außerhalb von <c>/api</c>: <c>[FromForm]</c> verlangt das Antiforgery-Token (Formularfeld von Blazor).</summary>
    /// <summary>Dazu ein GET, der einen beliebigen Status ohne Körper liefert.</summary>
    public void AbbildenAusserhalbApi(IEndpointRouteBuilder app)
    {
        app.MapPost("/test/formular", ([FromForm] string? wert) => TypedResults.Ok(new { wert }));
        app.MapGet("/test/status/{code:int}", (int code) => Results.StatusCode(code));
    }

    public static void Registrieren(IServiceCollection services) => services.AddSingleton<IZusatzEndpunkte, TestEndpunkte>();
}

internal static class TestHelfer
{
    public static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    /// <summary>Holt ein Antiforgery-Token; das Cookie merkt sich der Client.</summary>
    public static async Task<string> AntiforgeryTokenAsync(HttpClient client)
    {
        var antwort = await client.GetFromJsonAsync<AntiforgeryAntwort>("/api/betrieb/antiforgery", Abbruch);
        return antwort!.Token;
    }

    public static HttpRequestMessage FehlerMeldung(object koerper, string? antiforgery = null, string? dienst = null)
    {
        var anfrage = new HttpRequestMessage(HttpMethod.Post, "/api/betrieb/fehler") { Content = JsonContent.Create(koerper) };
        if (antiforgery is not null)
        {
            anfrage.Headers.Add(Kopfzeilen.Antiforgery, antiforgery);
        }

        if (dienst is not null)
        {
            anfrage.Headers.Add(Kopfzeilen.Dienst, dienst);
        }

        return anfrage;
    }

    public static BrowserFehler GueltigeMeldung() => new("TypeError", "/app/start", "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01");

    public static async Task<JsonElement> ProblemAsync(HttpResponseMessage antwort)
    {
        Assert.Equal("application/problem+json", antwort.Content.Headers.ContentType?.MediaType);
        return await antwort.Content.ReadFromJsonAsync<JsonElement>(Abbruch);
    }

    public static string? Code(JsonElement problem) =>
        problem.TryGetProperty("code", out var code) ? code.GetString() : null;
}
