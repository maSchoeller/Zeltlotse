using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Zeltlotse.Api.Contracts.Betrieb;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Web;

namespace Zeltlotse.Api.Betrieb;

/// <summary>Anonyme Betriebs-Endpunkte des API-Hosts unter <c>/api/betrieb</c>.</summary>
internal static class BetriebEndpunkte
{
    public static IEndpointRouteBuilder MapBetriebEndpoints(this IEndpointRouteBuilder api)
    {
        var betrieb = api.MapGroup("/betrieb");
        betrieb.MapGet("/antiforgery", AntiforgeryEndpunkt.Holen).Zugriffsbegrenzt();
        betrieb.MapPost("/fehler", BrowserFehlerEndpunkt.Melden).Zugriffsbegrenzt();
        return api;
    }
}

/// <summary><c>GET /api/betrieb/antiforgery</c>: Token im Körper, Cookie <c>__Host-Zeltlotse-Antiforgery</c> (AD-25).</summary>
internal static class AntiforgeryEndpunkt
{
    public static Ok<AntiforgeryAntwort> Holen(HttpContext kontext, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(kontext);
        return TypedResults.Ok(new AntiforgeryAntwort(tokens.RequestToken!));
    }
}

/// <summary>
/// <c>POST /api/betrieb/fehler</c>: unerwarteter Fehler im Browser. Geloggt werden nur Fehlerart, bereinigte Seite
/// und Trace-ID (Konvention „Logging“, AD-22).
/// </summary>
internal static partial class BrowserFehlerEndpunkt
{
    public static NoContent Melden(BrowserFehler? meldung, IValidator<BrowserFehler> validator, ILogger<BrowserFehler> logger)
    {
        meldung ??= new BrowserFehler(null, null, null);
        var ergebnis = validator.Validate(meldung);
        if (!ergebnis.IsValid)
        {
            throw ValidierungsAusnahme.Aus(ergebnis.Errors);
        }

        Gemeldet(logger, meldung.Fehlerart!, PfadBereinigung.Bereinigen(meldung.Seite), meldung.TraceId!);
        return TypedResults.NoContent();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Fehler im Browser: {Fehlerart} auf {Seite} (Trace {BrowserTraceId}).")]
    private static partial void Gemeldet(ILogger logger, string fehlerart, string seite, string browserTraceId);
}

/// <summary>Formprüfung der Browser-Meldung: Fehlerart und Trace-ID sind kurze Kennungen, nie Freitext.</summary>
internal sealed partial class BrowserFehlerValidator : AbstractValidator<BrowserFehler>
{
    public BrowserFehlerValidator()
    {
        RuleFor(m => m.Fehlerart).NotEmpty().MaximumLength(100).Matches(FehlerartMuster())
            .WithMessage("Nur Buchstaben, Ziffern und ._:- erlaubt.");
        RuleFor(m => m.Seite).NotEmpty().MaximumLength(2000).Matches(SeitenMuster())
            .WithMessage("Nur ein Pfad aus URL-Zeichen, beginnend mit /.");
        RuleFor(m => m.TraceId).NotEmpty().MaximumLength(100).Matches(TraceIdMuster())
            .WithMessage("Nur Buchstaben, Ziffern, : und - erlaubt.");
    }

    [GeneratedRegex("^[A-Za-z0-9._:-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex FehlerartMuster();

    /// <summary>Pfad nur aus URL-Zeichen (kein Leerzeichen, kein @, keine Steuerzeichen); Abfrage und Fragment fallen beim Bereinigen weg.</summary>
    [GeneratedRegex(@"^/[A-Za-z0-9/._~%-]*([?#][!-?A-~]*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex SeitenMuster();

    /// <summary>W3C-Trace-ID oder <c>HttpContext.TraceIdentifier</c> (enthält <c>:</c>).</summary>
    [GeneratedRegex("^[A-Za-z0-9:-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex TraceIdMuster();
}
