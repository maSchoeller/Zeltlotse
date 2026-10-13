using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Autosave;
using Zeltlotse.Ui.Kontext;

namespace Zeltlotse.Ui;

/// <summary>Registrierung der gemeinsamen App-Bausteine (AD-29) für Web-App und öffentliche Seiten.</summary>
public static class UiRegistrierung
{
    /// <summary>
    /// Registriert die typisierten API-Clients, den Freizeit-Kontext und den Autosave-Dienst (je Scope). Jeder Client
    /// spricht über den <see cref="HttpClient"/> aus <paramref name="client"/>; der Client bringt den Transport des
    /// jeweiligen Clients mit (Web-App: Antiforgery, Version, Verbindung; öffentliche Seiten: Dienst-Kennung, Besucher-IP).
    /// </summary>
    public static IServiceCollection AddZeltlotseUi(this IServiceCollection services, Func<IServiceProvider, HttpClient> client)
    {
        ArgumentNullException.ThrowIfNull(client);
        services.AddScoped(sp => new BetriebApiClient(client(sp)));
        services.AddScoped(sp => new FreizeitenApiClient(client(sp)));
        services.AddScoped(sp => new KontenApiClient(client(sp)));
        services.AddScoped(sp => new OrganisationenApiClient(client(sp)));
        services.AddScoped(sp => new PersonenApiClient(client(sp)));
        services.AddScoped(sp => new AutosaveDienst(client(sp)));
        services.AddScoped<FreizeitKontextDienst>();
        // Story 2-5: „Einsicht bis“ bzw. „Einsicht beendet am“ hängt an der Uhr; Web-App und Host bringen ihre eigene mit.
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
}
