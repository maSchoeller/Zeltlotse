using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zeltlotse.App.Browser;
using Zeltlotse.App.Offline;
using Zeltlotse.App.Verbindung;
using Zeltlotse.Ui.Transport;

namespace Zeltlotse.Ui.Tests;

/// <summary>
/// Story 6-5: Registriert für Seiten-Tests, was die offline nutzbaren Seiten brauchen: Browser (falls noch keiner registriert ist), Verbindung,
/// Offline-Speicher und -Abgleich. Die Sitzung und die API-Clients registriert der Test selbst.
/// </summary>
internal static class OfflineTestDienste
{
    public static IServiceCollection AddOfflineDienste(this IServiceCollection services, IBrowser? browser = null)
    {
        if (browser is not null)
        {
            services.AddSingleton(browser);
        }
        else
        {
            services.TryAddSingleton<IBrowser>(sp => new NachgebildeterBrowser(sp.GetService<TimeProvider>()));
        }

        services.TryAddSingleton<AppVerbindung>();
        services.TryAddSingleton<IVerbindung>(sp => sp.GetRequiredService<AppVerbindung>());
        services.TryAddScoped<OfflineSpeicher>();
        services.TryAddScoped<OfflineAbgleich>();
        return services;
    }
}
