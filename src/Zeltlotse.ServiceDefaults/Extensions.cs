using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Gemeinsame Aspire-Grundlagen: Service Discovery, Resilienz, Health Checks und OpenTelemetry.
/// Siehe https://aka.ms/aspire/service-defaults.
/// </summary>
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;

            // AD-22: Scopes von ASP.NET Core tragen den Rohpfad (RequestPath) samt Token-Segmenten; sie gehen nicht in den Export.
            logging.IncludeScopes = false;

            // AD-22: Ausnahmen gehen nur mit ihrem Typ hinaus, nie mit Nachricht oder Stacktrace (auch die der Frameworks).
            logging.AddProcessor(new LogBereinigung());
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                // AD-22: Ausnahmen nicht als Span-Ereignis mit Nachricht aufzeichnen; Rohpfad, Abfrage und URL entfernt
                // die TelemetrieBereinigung vor jedem Exporter (eingehend und ausgehend), es bleibt nur http.route.
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(aspNetCore =>
                    {
                        aspNetCore.RecordException = false;
                        aspNetCore.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath);
                    })
                    .AddHttpClientInstrumentation(http => http.RecordException = false)
                    .AddProcessor(new TelemetrieBereinigung());
            });
        AusnahmeEreignisBereinigung.Aktivieren();

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    /// <summary>
    /// <c>/health</c> und <c>/alive</c>. Beide liefern nur den Gesamtstatus ohne Details,
    /// daher sind sie in allen Umgebungen eingebunden (Prüfung beim Start, Reverse Proxy).
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Nur lesende Methoden: Health-Endpunkte liegen außerhalb von /api und damit außerhalb des Antiforgery-Zwangs.
        string[] lesend = [HttpMethods.Get, HttpMethods.Head];
        app.MapHealthChecks(HealthEndpointPath).WithMetadata(new HttpMethodMetadata(lesend));

        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live"),
        }).WithMetadata(new HttpMethodMetadata(lesend));

        return app;
    }
}
