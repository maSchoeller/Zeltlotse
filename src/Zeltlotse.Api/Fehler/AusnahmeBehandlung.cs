using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Zeltlotse.Api.Auslieferung;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Pipeline;

namespace Zeltlotse.Api.Fehler;

/// <summary>
/// Fehlerform ProblemDetails (RFC 9457) mit <c>traceId</c> (AD-22, AD-27):
/// <see cref="ValidierungsAusnahme"/> → <c>400</c> mit Fehlern je JSON-Feldname; <see cref="SchalterAusAusnahme"/> → <c>404</c> ohne Details
/// (Story 3-7, AD-20); jede andere Ausnahme → <c>500</c>,
/// in Antwort und Log nur mit Ausnahmetyp und Trace-ID, nie mit Nachricht oder Stacktrace.
/// </summary>
internal sealed partial class AusnahmeBehandlung(IProblemDetailsService problemDetails, ILogger<AusnahmeBehandlung> logger) : IExceptionHandler
{
    public const string TraceIdFeld = "traceId";

    public async ValueTask<bool> TryHandleAsync(HttpContext kontext, Exception ausnahme, CancellationToken cancellationToken)
    {
        // Abgebrochen vom Client: kein Fehler des Servers, niemand liest die Antwort.
        if (ausnahme is OperationCanceledException && kontext.RequestAborted.IsCancellationRequested)
        {
            return true;
        }

        var traceId = TraceId(kontext);
        var problem = ausnahme switch
        {
            ValidierungsAusnahme validierung => Validierungsproblem(validierung),
            // Story 3-7 (AD-20): wie ein abgeschalteter Endpunkt, ohne Details.
            SchalterAusAusnahme => new ProblemDetails { Status = StatusCodes.Status404NotFound },
            BadHttpRequestException anfrage => new ProblemDetails
            {
                Status = anfrage.StatusCode,
                Title = "Die Anfrage ist ungültig.",
                Extensions = { [Problemcodes.Feld] = Problemcodes.Validierung },
            },
            _ => Unerwartet(ausnahme, traceId),
        };
        problem.Extensions[TraceIdFeld] = traceId;
        kontext.Response.StatusCode = problem.Status!.Value;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = kontext, ProblemDetails = problem });
    }

    /// <summary>Wie der eingebaute ProblemDetails-Schreiber: Trace-ID der Aktivität, sonst die Kennung der Anfrage.</summary>
    public static string TraceId(HttpContext kontext) => Activity.Current?.Id ?? kontext.TraceIdentifier;

    private static HttpValidationProblemDetails Validierungsproblem(ValidierungsAusnahme validierung) =>
        new(validierung.Fehler.ToDictionary(f => f.Key, f => f.Value))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = validierung.Message,
            Extensions = { [Problemcodes.Feld] = Problemcodes.Validierung },
        };

    private ProblemDetails Unerwartet(Exception ausnahme, string traceId)
    {
        var typ = ausnahme.GetType().Name;
        UnerwarteteAusnahme(typ, traceId);
        return new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Ein unerwarteter Fehler ist aufgetreten.",
            Extensions =
            {
                [Problemcodes.Feld] = Problemcodes.UnerwarteterFehler,
                ["ausnahmeTyp"] = typ,
            },
        };
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unerwartete Ausnahme {AusnahmeTyp} (Trace {FehlerTraceId}).")]
    private partial void UnerwarteteAusnahme(string ausnahmeTyp, string fehlerTraceId);
}

internal static class FehlerRegistrierung
{
    public static IServiceCollection AddFehlerform(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddExceptionHandler<AusnahmeBehandlung>();

        // Bindungsfehler (z. B. kein gültiges JSON) als BadHttpRequestException, damit sie die einheitliche Fehlerform bekommen.
        services.Configure<RouteHandlerOptions>(optionen => optionen.ThrowOnBadRequest = true);

        // Die eingebaute Ausnahme-Protokollierung gibt Nachricht und Stacktrace aus: abgeschaltet (AD-22).
        services.Configure<ExceptionHandlerOptions>(optionen => optionen.SuppressDiagnosticsCallback = _ => true);
        return services;
    }

    /// <summary>Ausnahmen als ProblemDetails; Statuscode-Seiten unter <c>/api</c> als ProblemDetails, sonst als HTML-Seite.</summary>
    public static IApplicationBuilder UseFehlerform(this IApplicationBuilder app) =>
        app.UseExceptionHandler().UseStatuscodeSeiten();
}
