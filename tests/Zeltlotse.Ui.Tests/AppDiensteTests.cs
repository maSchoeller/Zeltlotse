using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Betrieb;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.App;
using Zeltlotse.App.Browser;
using Zeltlotse.App.Verbindung;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Transport;

namespace Zeltlotse.Ui.Tests;

/// <summary>Die Handler-Kette aus <see cref="AppDienste.AddAppDienste(IServiceCollection, Uri)"/>: Verbindung, Version, Antiforgery.</summary>
public sealed class AppDiensteTests
{
    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    private static (AsyncServiceScope Scope, NachgebildeterHandler Server) Dienste(NachgebildeterHandler server)
    {
        var services = new ServiceCollection();
        services.AddAppDienste(new Uri("http://localhost/app/"), () => server);
        services.AddSingleton<IBrowser>(new NachgebildeterBrowser());
        return (services.BuildServiceProvider().CreateAsyncScope(), server);
    }

    [Fact]
    public async Task Netzfehler_erreicht_die_Verbindung_und_die_App_ist_offline()
    {
        var (scope, _) = Dienste(new NachgebildeterHandler().Bei(r => true, _ => throw new HttpRequestException("Kein Netz")));
        await using var _scope = scope;

        await Assert.ThrowsAsync<HttpRequestException>(() => scope.ServiceProvider.GetRequiredService<BetriebApiClient>().AntiforgeryTokenHolenAsync(Abbruch));

        Assert.True(scope.ServiceProvider.GetRequiredService<AppVerbindung>().Offline);
    }

    [Fact]
    public async Task POST_traegt_das_Antiforgery_Token_und_eine_andere_Server_Version_setzt_NeueVersion()
    {
        var server = new NachgebildeterHandler()
            .Bei(HttpMethod.Get, "/api/betrieb/antiforgery", () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"token":"abc"}"""))
            .Bei(HttpMethod.Post, "/api/betrieb/fehler", () =>
            {
                var antwort = new HttpResponseMessage(HttpStatusCode.NoContent);
                antwort.Headers.Add(Kopfzeilen.Version, "999.0.0+anders");
                return antwort;
            });
        var (scope, _) = Dienste(server);
        await using var _scope = scope;

        await scope.ServiceProvider.GetRequiredService<BetriebApiClient>()
            .FehlerMeldenAsync(new BrowserFehler("TypeError", "/app/", "0af7651916cd43dd8448eb211c80319c"), Abbruch);

        var post = Assert.Single(server.Anfragen, a => a.Methode == HttpMethod.Post);
        Assert.Equal("abc", post.Kopfzeilen[Kopfzeilen.Antiforgery]);
        Assert.True(scope.ServiceProvider.GetRequiredService<NeueVersion>().Da);
        Assert.False(scope.ServiceProvider.GetRequiredService<AppVerbindung>().Offline);
    }
}
