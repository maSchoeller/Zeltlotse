using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Adapter;
using Zeltlotse.SharedKernel.Push;
using Zeltlotse.Testing;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 6-1: der echte Web-Push-Adapter <see cref="WebPushVersand"/> mit einem aufzeichnenden Handler statt des Netzes: 404 und 410 heißen „Ziel ungültig“,
/// andere Fehler werfen, ein nicht erlaubter Endpunkt geht nie hinaus, Weiterleitungen gelten nicht als Erfolg, die Anfrage trägt VAPID und TTL; dazu die
/// Registrierung (kurze Wartezeit, der registrierte Client folgt keinen Weiterleitungen).
/// </summary>
public sealed class WebPushVersandTests
{
    private readonly Aufzeichnung _handler = new();

    private readonly VapidSchluessel _vapid = VapidSchluessel.AusBase64(ZeltlotseApiFactory.NeuerVapidSchluessel());

    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    private sealed class Aufzeichnung : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.Created;

        public List<HttpRequestMessage> Anfragen { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Anfragen.Add(request);
            var antwort = new HttpResponseMessage(Status) { RequestMessage = request };
            if ((int)Status is >= 300 and < 400)
            {
                antwort.Headers.Location = new Uri("https://127.0.0.1/umgeleitet");
            }

            return Task.FromResult(antwort);
        }
    }

    private WebPushVersand Versand()
    {
        var services = new ServiceCollection();
        services.AddHttpClient(WebPushVersand.ClientName).ConfigurePrimaryHttpMessageHandler(() => _handler);
        var konfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Zeltlotse:OeffentlicheAdresse"] = "https://zeltlotse.example.org" })
            .Build();
        var dienste = services.BuildServiceProvider();
        return new WebPushVersand(dienste.GetRequiredService<IHttpClientFactory>(), _vapid, konfiguration);
    }

    private static PushZiel Ziel(string endpunkt = "https://fcm.googleapis.com/fcm/send/geraet")
    {
        using var browser = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var punkt = browser.ExportParameters(includePrivateParameters: false).Q;
        return new PushZiel(Guid.CreateVersion7(), endpunkt, Base64Url([0x04, .. punkt.X!, .. punkt.Y!]), Base64Url(RandomNumberGenerator.GetBytes(16)));
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] AusBase64Url(string wert)
    {
        var base64 = wert.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '='));
    }

    private static PushInhalt Inhalt => new("Zeltlager · Team", "Feuerholz holen", "/app/freizeiten/x/kanaele");

    [Fact]
    public async Task Zugestellt_mit_VAPID_Authorization_und_TTL_an_den_Endpunkt()
    {
        var ergebnis = await Versand().SendenAsync(Ziel(), Inhalt, Abbruch);

        Assert.Equal(PushErgebnis.Zugestellt, ergebnis);
        var anfrage = Assert.Single(_handler.Anfragen);
        Assert.Equal((HttpMethod.Post, "https://fcm.googleapis.com/fcm/send/geraet"), (anfrage.Method, anfrage.RequestUri!.ToString()));
        Assert.Equal("vapid", anfrage.Headers.Authorization?.Scheme, ignoreCase: true);
        Assert.True(anfrage.Headers.TryGetValues("TTL", out var ttl));
        Assert.Equal("86400", Assert.Single(ttl));
    }

    [Fact]
    public async Task VAPID_traegt_die_oeffentliche_Adresse_als_sub_den_Push_Dienst_als_aud_und_den_eigenen_Schluessel()
    {
        await Versand().SendenAsync(Ziel(), Inhalt, Abbruch);

        var teile = Assert.Single(_handler.Anfragen).Headers.Authorization!.Parameter!
            .Split(',', StringSplitOptions.TrimEntries)
            .Select(teil => teil.Split('=', 2))
            .ToDictionary(teil => teil[0], teil => teil[1]);
        Assert.Equal(_vapid.Oeffentlich, teile["k"]);
        using var nutzlast = JsonDocument.Parse(Encoding.UTF8.GetString(AusBase64Url(teile["t"].Split('.')[1])));
        Assert.Equal("https://zeltlotse.example.org", nutzlast.RootElement.GetProperty("sub").GetString());
        Assert.Equal("https://fcm.googleapis.com", nutzlast.RootElement.GetProperty("aud").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Gone)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Unbekanntes_Abo_beim_Push_Dienst_ist_ein_ungueltiges_Ziel(HttpStatusCode status)
    {
        _handler.Status = status;

        Assert.Equal(PushErgebnis.ZielUngueltig, await Versand().SendenAsync(Ziel(), Inhalt, Abbruch));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    public async Task Andere_Fehler_und_Weiterleitungen_werfen_ohne_der_Weiterleitung_zu_folgen(HttpStatusCode status)
    {
        _handler.Status = status;

        await Assert.ThrowsAnyAsync<Exception>(() => Versand().SendenAsync(Ziel(), Inhalt, Abbruch));
        Assert.Single(_handler.Anfragen);
    }

    [Theory]
    [InlineData("https://example.org/push")]
    [InlineData("https://127.0.0.1/push")]
    [InlineData("http://fcm.googleapis.com/fcm/send/x")]
    public async Task Nicht_erlaubter_Endpunkt_ist_ungueltig_ohne_Anfrage(string endpunkt)
    {
        Assert.Equal(PushErgebnis.ZielUngueltig, await Versand().SendenAsync(Ziel(endpunkt), Inhalt, Abbruch));
        Assert.Empty(_handler.Anfragen);
    }

    [Fact]
    public void Registrierung_kurze_Wartezeit_und_keine_Weiterleitungen()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddPushVersand();
        using var dienste = services.BuildServiceProvider();

        using var client = dienste.GetRequiredService<IHttpClientFactory>().CreateClient(WebPushVersand.ClientName);
        Assert.Equal(TimeSpan.FromSeconds(10), client.Timeout);

        // Der Handler, den der registrierte Client wirklich nutzt: die Kette bis zum innersten Handler.
        HttpMessageHandler handler = dienste.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(WebPushVersand.ClientName);
        while (handler is DelegatingHandler kette)
        {
            handler = kette.InnerHandler!;
        }

        var primaer = Assert.IsType<SocketsHttpHandler>(handler);
        Assert.False(primaer.AllowAutoRedirect);
    }
}
