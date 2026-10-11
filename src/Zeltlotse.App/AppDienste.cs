using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.App.Browser;
using Zeltlotse.App.Verbindung;
using Zeltlotse.Ui;
using Zeltlotse.Ui.Transport;

namespace Zeltlotse.App;

/// <summary>Dienste der Web-App; in WebAssembly entspricht ein Scope der Lebensdauer der App.</summary>
public static class AppDienste
{
    /// <summary>
    /// Registriert Browser-Schnittstelle, Verbindungszustand, Versions-Hinweis und den Transport aller API-Clients:
    /// Verbindungs-Handler → Versions-Handler → Antiforgery-Handler → Browser (fetch, gleiche Adresse).
    /// </summary>
    public static IServiceCollection AddAppDienste(this IServiceCollection services, Uri basisAdresse) =>
        services.AddAppDienste(basisAdresse, () => new HttpClientHandler());

    /// <summary>Wie oben, mit eigenem innersten Handler (Tests).</summary>
    internal static IServiceCollection AddAppDienste(this IServiceCollection services, Uri basisAdresse, Func<HttpMessageHandler> innen)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IBrowser, JsBrowser>();
        services.AddScoped<AppVerbindung>();
        services.AddScoped<IVerbindung>(sp => sp.GetRequiredService<AppVerbindung>());
        services.AddScoped<NeueVersion>();
        services.AddScoped(sp => new HttpClient(
            new VerbindungsHandler(sp.GetRequiredService<IVerbindung>())
            {
                InnerHandler = new VersionsHandler(sp.GetRequiredService<NeueVersion>())
                {
                    InnerHandler = new AntiforgeryHandler { InnerHandler = innen() },
                },
            })
        {
            BaseAddress = basisAdresse,

            // Das Zeitlimit setzt der Verbindungs-Handler (Zeitüberschreitung gilt dort als Netzfehler).
            Timeout = Timeout.InfiniteTimeSpan,
        });
        services.AddZeltlotseUi(sp => sp.GetRequiredService<HttpClient>());
        return services;
    }
}
