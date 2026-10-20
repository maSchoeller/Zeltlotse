using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zeltlotse.SharedKernel.Dateien;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.SharedKernel.Mail;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Outbox;
using Zeltlotse.SharedKernel.Push;

namespace Zeltlotse.SharedKernel;

public static class SharedKernelRegistrierung
{
    /// <summary>
    /// Uhr, Mandanten-Kontext, Ereignis-Katalog, Outbox-Zustellung und zentrale Verschlüsselung. Mehrfacher Aufruf ist unschädlich.
    /// Die Verschlüsselung braucht eine <see cref="IPersonenSchluesselQuelle"/> (registriert das Modul Personen).
    /// </summary>
    public static IServiceCollection AddSharedKernel(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<MandantenKontext>();
        services.TryAddScoped<IMandantenKontext>(sp => sp.GetRequiredService<MandantenKontext>());
        services.TryAddSingleton<EreignisKatalog>();
        services.TryAddSingleton<MailKatalog>();
        services.TryAddSingleton<PushKatalog>();
        services.TryAddSingleton<IOutboxZustellung, OutboxZustellung>();
        services.TryAddSingleton(sp => Hauptschluessel.AusKonfiguration(sp.GetRequiredService<IConfiguration>()));
        services.TryAddScoped<IVerschluesselung, Verschluesselung>();
        // Story 6-1: der VAPID-Schlüssel für Web-Push (AD-21); erst beim ersten Gebrauch gelesen, die Startprüfung des API-Hosts liest ihn sofort.
        services.TryAddSingleton(sp => VapidSchluessel.AusKonfiguration(sp.GetRequiredService<IConfiguration>()));
        // Über eine Fabrik: Den Dateispeicher (Adapter) registriert erst der API-Host (AD-23).
        services.TryAddScoped<IGeschuetzteDateien>(sp => new GeschuetzteDateien(
            sp.GetRequiredService<IDateiSpeicher>(), sp.GetRequiredService<IVerschluesselung>()));
        return services;
    }

    /// <summary>Periodischer Hintergrunddienst, der die Outboxen aller Module zustellt (nur im API-Host).</summary>
    public static IServiceCollection AddOutboxZustellDienst(this IServiceCollection services)
    {
        services.AddSharedKernel();
        services.AddHostedService<ZustellDienst>();
        return services;
    }
}
