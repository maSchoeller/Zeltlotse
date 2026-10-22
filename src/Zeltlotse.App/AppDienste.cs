using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.App.Browser;
using Zeltlotse.App.Features.Konten;
using Zeltlotse.App.Offline;
using Zeltlotse.App.Sitzung;
using Zeltlotse.App.Verbindung;
using Zeltlotse.Ui;
using Zeltlotse.Ui.Transport;

namespace Zeltlotse.App;

/// <summary>Dienste der Web-App; in WebAssembly entspricht ein Scope der Lebensdauer der App.</summary>
public static class AppDienste
{
    /// <summary>
    /// Registriert Browser-Schnittstelle, Verbindungszustand, Versions-Hinweis, Sitzung, Offline-Speicher und -Abgleich (Story 6-5) und den
    /// Transport aller API-Clients:
    /// Verbindungs-Handler → Versions-Handler → Sitzungs-Handler → Antiforgery-Handler → Browser (fetch, gleiche Adresse,
    /// mit dem HttpOnly-Sitzungs-Cookie).
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
        services.AddScoped<AppSitzung>();
        services.AddScoped<ISitzung>(sp => sp.GetRequiredService<AppSitzung>());
        services.AddScoped(_ => new AntiforgeryHandler { InnerHandler = innen() });
        services.AddScoped<AnmeldungAbschluss>();
        services.AddScoped(sp => new HttpClient(
            new VerbindungsHandler(sp.GetRequiredService<IVerbindung>())
            {
                InnerHandler = new VersionsHandler(sp.GetRequiredService<NeueVersion>())
                {
                    InnerHandler = new SitzungsHandler(sp.GetRequiredService<ISitzung>())
                    {
                        InnerHandler = sp.GetRequiredService<AntiforgeryHandler>(),
                    },
                },
            },
            disposeHandler: false)
        {
            BaseAddress = basisAdresse,

            // Das Zeitlimit setzt der Verbindungs-Handler (Zeitüberschreitung gilt dort als Netzfehler).
            Timeout = Timeout.InfiniteTimeSpan,
        });
        services.AddZeltlotseUi(sp => sp.GetRequiredService<HttpClient>());
        // Story 6-5: Offline-Speicher (verschlüsselt in IndexedDB) und der Abgleich mit Warteschlange des Vorfall-Protokolls.
        services.AddScoped<OfflineSpeicher>();
        services.AddScoped<OfflineAbgleich>();
        return services;
    }
}
