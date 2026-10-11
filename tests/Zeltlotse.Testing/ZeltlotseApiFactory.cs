using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Zeltlotse.Api.Betrieb;
using Zeltlotse.SharedKernel.Outbox;

namespace Zeltlotse.Testing;

/// <summary>
/// Startet den API-Host gegen eine Test-Datenbank, mit künstlicher Uhr und ohne periodische Hintergrunddienste.
/// Die Outbox wird in Tests ausdrücklich mit <see cref="OutboxJetztZustellenAsync"/> abgearbeitet.
/// Der Client spricht <c>https://localhost</c>, damit <c>Secure</c>-Cookies gesetzt und zurückgeschickt werden.
/// </summary>
/// <param name="verbindungsZeichenfolge">Test-Datenbank.</param>
/// <param name="mitZustellDienst">Nur für Tests der Host-Verdrahtung: periodische Hintergrunddienste nicht entfernen.</param>
public sealed class ZeltlotseApiFactory : WebApplicationFactory<Program>
{
    private readonly string _verbindungsZeichenfolge;
    private readonly bool _mitZustellDienst;

    public ZeltlotseApiFactory(string verbindungsZeichenfolge, bool mitZustellDienst = false)
    {
        _verbindungsZeichenfolge = verbindungsZeichenfolge;
        _mitZustellDienst = mitZustellDienst;
        ClientOptions.BaseAddress = new Uri("https://localhost");
    }

    /// <summary>Kopfzeile, mit der ein Test die Absender-IP der Verbindung festlegt (nur im Testserver).</summary>
    public const string TestAbsenderIp = "X-Test-Absender-Ip";

    public FakeTimeProvider Uhr { get; } = new(new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero));

    /// <summary>Umgebung des Hosts; nur Tests der Startprüfung setzen etwas anderes als <c>Testing</c>.</summary>
    public string Umgebung { get; init; } = "Testing";

    /// <summary>
    /// Einstellungen des Hosts. Vorbelegt mit je Testlauf zufälligem Hauptschlüssel und HMAC-Schlüssel;
    /// ein leerer Wert gilt als fehlend.
    /// </summary>
    public Dictionary<string, string?> Einstellungen { get; } = new()
    {
        ["Zeltlotse:Verschluesselung:Hauptschluessel"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        ["Zeltlotse:Protokoll:HmacSchluessel"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
    };

    /// <summary>Zusätzliche Registrierungen nur für Tests (z. B. Test-Endpunkte, Exporter, Fake-Logger).</summary>
    public List<Action<IServiceCollection>> ZusatzDienste { get; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Umgebung);
        builder.UseSetting("ConnectionStrings:zeltlotse", _verbindungsZeichenfolge);
        foreach (var (schluessel, wert) in Einstellungen)
        {
            builder.UseSetting(schluessel, wert);
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Uhr);
            services.AddTransient<IStartupFilter, AbsenderIpSetzen>();

            foreach (var zusatz in ZusatzDienste)
            {
                zusatz(services);
            }

            if (_mitZustellDienst)
            {
                return;
            }

            var hintergrundDienste = services
                .Where(d => d.ServiceType == typeof(IHostedService)
                            && (d.ImplementationType == typeof(ZustellDienst) || d.ImplementationType == typeof(SicherheitsprotokollFristDienst)))
                .ToList();
            foreach (var dienst in hintergrundDienste)
            {
                services.Remove(dienst);
            }
        });
    }

    /// <summary>Stellt alle fälligen Outbox-Ereignisse sofort zu, bis nichts mehr fällig ist.</summary>
    public Task OutboxJetztZustellenAsync(CancellationToken abbruch = default) =>
        Services.GetRequiredService<IOutboxZustellung>().AllesZustellenAsync(abbruch);

    /// <summary>Der Testserver kennt keine Absender-IP; Tests setzen sie über <see cref="TestAbsenderIp"/>.</summary>
    private sealed class AbsenderIpSetzen : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((kontext, weiter) =>
            {
                if (IPAddress.TryParse(kontext.Request.Headers[TestAbsenderIp], out var ip))
                {
                    kontext.Connection.RemoteIpAddress = ip;
                }

                return weiter(kontext);
            });
            next(app);
        };
    }
}
