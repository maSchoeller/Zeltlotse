using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Zeltlotse.Public.Transport;
using Zeltlotse.Ui;

namespace Zeltlotse.Public;

/// <summary>Einstellungen des API-Transports der öffentlichen Seiten.</summary>
public sealed class PublicSeitenOptionen
{
    /// <summary>Basisadresse der API; Vorgabe: die eigene Adresse des Hosts.</summary>
    public Uri? Basisadresse { get; set; }

    /// <summary>Primärer Handler; Vorgabe: ein gemeinsamer <see cref="SocketsHttpHandler"/>. Tests setzen den Handler des Testservers.</summary>
    public Func<HttpMessageHandler>? PrimaerHandler { get; set; }
}

/// <summary>Registrierung und Abbildung der öffentlichen Seiten unter <c>/</c> (AD-5): nur statisches Server-Rendering.</summary>
public static class PublicSeiten
{
    /// <summary>
    /// Registriert die Razor-Komponenten (ohne interaktive Render-Modi) und die API-Clients mit dem Transport der
    /// öffentlichen Seiten. <paramref name="dienstGeheimnis"/> liefert das Geheimnis der Dienst-Kennung des Hosts.
    /// </summary>
    public static IServiceCollection AddPublicSeiten(this IServiceCollection services, Func<IServiceProvider, string> dienstGeheimnis)
    {
        ArgumentNullException.ThrowIfNull(dienstGeheimnis);
        services.AddRazorComponents();
        services.AddHttpContextAccessor();
        services.AddOptions<PublicSeitenOptionen>();
        services.AddSingleton(sp => new PublicDienstKennung(dienstGeheimnis(sp)));
        services.AddSingleton<PublicApiTransport>();
        services.AddZeltlotseUi(sp => sp.GetRequiredService<PublicApiTransport>().NeuerClient());
        return services;
    }

    /// <summary>Bildet die Seiten ab (statisch, ohne interaktive Render-Modi).</summary>
    public static RazorComponentsEndpointConventionBuilder MapPublicSeiten(this IEndpointRouteBuilder app) =>
        app.MapRazorComponents<Wurzel>();

    /// <summary>Pfad der Seite „Diese Seite ist nicht verfügbar.“ für die Statuscode-Seiten des Hosts.</summary>
    public const string NichtVerfuegbarPfad = Seiten.NichtVerfuegbar.Pfad;
}

/// <summary>
/// Ein gemeinsamer primärer Handler je Prozess und je Scope ein <see cref="HttpClient"/> mit dem
/// <see cref="PublicTransportHandler"/> davor. Bewusst ohne die Resilienz-Vorgaben der HTTP-Client-Fabrik:
/// Wiederholungen würden ändernde Aufrufe doppelt senden und die Zugriffsbegrenzung doppelt zählen.
/// </summary>
internal sealed class PublicApiTransport : IDisposable
{
    private readonly IHttpContextAccessor _anfrage;
    private readonly PublicDienstKennung _kennung;
    private readonly Lazy<HttpMessageHandler> _primaer;
    private readonly Lazy<Uri> _basis;

    public PublicApiTransport(IOptions<PublicSeitenOptionen> optionen, IServer server, IHttpContextAccessor anfrage, PublicDienstKennung kennung)
    {
        _anfrage = anfrage;
        _kennung = kennung;
        _primaer = new(() => optionen.Value.PrimaerHandler?.Invoke()
                             ?? new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });
        _basis = new(() => optionen.Value.Basisadresse ?? EigeneAdresse(server));
    }

    public HttpClient NeuerClient() =>
        new(new PublicTransportHandler(_anfrage, _kennung) { InnerHandler = _primaer.Value }, disposeHandler: false)
        {
            BaseAddress = _basis.Value,
        };

    /// <summary>Die eigene Adresse des Hosts; Platzhalter-Adressen (alle Schnittstellen) werden zu Loopback.</summary>
    internal static Uri EigeneAdresse(IServer server)
    {
        var adressen = server.Features.Get<IServerAddressesFeature>()?.Addresses.ToList() ?? [];
        var adresse = adressen.FirstOrDefault(a => a.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                      ?? adressen.FirstOrDefault()
                      ?? throw new InvalidOperationException("Der Host kennt keine eigene Adresse für die öffentlichen Seiten.");
        var bindung = BindingAddress.Parse(adresse);
        var host = bindung.Host switch
        {
            "+" or "*" or "[::]" or "0.0.0.0" => IPAddress.Loopback.ToString(),
            var h => h,
        };
        return new UriBuilder(bindung.Scheme, host, bindung.Port).Uri;
    }

    public void Dispose()
    {
        if (_primaer.IsValueCreated)
        {
            _primaer.Value.Dispose();
        }
    }
}
