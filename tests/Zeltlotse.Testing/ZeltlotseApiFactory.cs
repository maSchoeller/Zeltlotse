using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Zeltlotse.SharedKernel.Outbox;

namespace Zeltlotse.Testing;

/// <summary>
/// Startet den API-Host gegen eine Test-Datenbank, mit künstlicher Uhr und ohne periodischen Zustell-Dienst.
/// Die Outbox wird in Tests ausdrücklich mit <see cref="OutboxJetztZustellenAsync"/> abgearbeitet.
/// </summary>
/// <param name="verbindungsZeichenfolge">Test-Datenbank.</param>
/// <param name="mitZustellDienst">Nur für Tests der Host-Verdrahtung: periodischen Zustell-Dienst nicht entfernen.</param>
public sealed class ZeltlotseApiFactory(string verbindungsZeichenfolge, bool mitZustellDienst = false) : WebApplicationFactory<Program>
{
    public FakeTimeProvider Uhr { get; } = new(new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:zeltlotse", verbindungsZeichenfolge);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Uhr);

            if (mitZustellDienst)
            {
                return;
            }

            var zustellDienste = services
                .Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(ZustellDienst))
                .ToList();
            foreach (var dienst in zustellDienste)
            {
                services.Remove(dienst);
            }
        });
    }

    /// <summary>Stellt alle fälligen Outbox-Ereignisse sofort zu, bis nichts mehr fällig ist.</summary>
    public Task OutboxJetztZustellenAsync(CancellationToken abbruch = default) =>
        Services.GetRequiredService<IOutboxZustellung>().AllesZustellenAsync(abbruch);
}
