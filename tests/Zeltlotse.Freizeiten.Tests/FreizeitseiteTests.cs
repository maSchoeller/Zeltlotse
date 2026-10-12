using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zeltlotse.Freizeiten.Daten;
using Zeltlotse.Freizeiten.Domaene;
using Zeltlotse.Freizeiten.Freizeitseite;
using Zeltlotse.Freizeiten.Verwaltung;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Zugangslinks;
using Zeltlotse.Testing;
using ApiFeld = Zeltlotse.Api.Contracts.Freizeiten.FreizeitseiteFeld;

namespace Zeltlotse.Freizeiten.Tests;

/// <summary>
/// Story 2-1: Platzlage und „Ausgebucht“ (FR-67), die Anmeldelinks einer Freizeit (AD-11) und der Mandantenfilter des Links. Weil
/// es vor Story 2-3 keine bestätigten Anmeldungen gibt, ersetzt der HTTP-Test <see cref="IBestaetigteTeilnehmende"/>.
/// </summary>
public sealed class FreizeitseiteTests(PostgresContainer postgres) : IAsyncLifetime
{
    private static readonly Guid OrgA = Guid.CreateVersion7();
    private static readonly Guid OrgB = Guid.CreateVersion7();
    private static readonly Guid Andrea = Guid.CreateVersion7();

    private readonly FesteBestaetigte _bestaetigt = new();
    private ZeltlotseApiFactory _factory = null!;
    private string _verbindung = null!;

    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _verbindung = await postgres.NeueDatenbankAsync();
        _factory = new ZeltlotseApiFactory(_verbindung);
        _factory.ZusatzDienste.Add(s => s.Replace(ServiceDescriptor.Singleton<IBestaetigteTeilnehmende>(_bestaetigt)));
        _ = _factory.CreateClient();
        await AusfuehrenAsync(
            "INSERT INTO organisationen.organisation (\"Id\", \"Name\", \"AngelegtAm\") VALUES (@id, 'CVJM Musterstadt', now())", ("id", OrgA));
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    // ---------- Platzlage ----------

    [Theory]
    [InlineData(null, 0, "Teilnehmende", false)]
    [InlineData(null, 500, "Teilnehmende", false)]
    [InlineData(40, 39, "Teilnehmende", false)]
    [InlineData(40, 40, "Teilnehmende", true)]
    [InlineData(40, 41, "Teilnehmende", true)]
    [InlineData(40, 40, "Mitarbeitende", false)]
    [InlineData(1, 5, "Mitarbeitende", false)]
    public void Ausgebucht_nur_fuer_Teilnehmende_mit_Hoechstzahl_und_genug_Bestaetigten(int? hoechstzahl, int bestaetigt, string art, bool erwartet) =>
        Assert.Equal(erwartet, Platzlage.Ausgebucht(hoechstzahl, bestaetigt, Enum.Parse<LinkArt>(art)));

    [Theory]
    [InlineData("Miriam", "Zeltner", "MZ")]
    [InlineData(" jonas ", null, "J")]
    [InlineData("Özlem", "Yilmaz", "ÖY")]
    [InlineData("\U0001D504nna", "émile", "\U0001D504É")]
    public void Initialen_nehmen_das_erste_Schriftzeichen_auch_zusammengesetzt_und_ausserhalb_der_Grundebene(string vorname, string? nachname, string erwartet) =>
        Assert.Equal(erwartet, FreizeitseiteOeffentlichEndpunkt.Initialen(vorname, nachname));

    [Fact]
    public async Task Ohne_Anmeldungen_gibt_es_noch_keine_bestaetigten_Teilnehmenden()
    {
        await using var scope = _factory.Services.CreateAsyncScope();

        Assert.Equal(0, await new KeineBestaetigtenTeilnehmenden().AnzahlAsync(Guid.CreateVersion7(), Abbruch));
        Assert.IsType<FesteBestaetigte>(scope.ServiceProvider.GetRequiredService<IBestaetigteTeilnehmende>());
    }

    // ---------- Anmeldelinks ----------

    [Fact]
    public async Task Anlegen_erzeugt_beide_Anmeldelinks_mit_Hash_und_lesbarem_Chiffretext()
    {
        var freizeit = await AnlegenAsync();

        var links = await LinksAsync(freizeit);

        Assert.Equal([LinkArt.Teilnehmende, LinkArt.Mitarbeitende], links.Select(l => l.Art));
        foreach (var link in links)
        {
            var token = await TokenAsync(link);
            Assert.True(Zugangslink.KannTokenSein(token));
            Assert.Equal(Zugangslink.TokenHash(Anmeldelink.Zweck(link.Art), token), link.TokenHash);
            Assert.Equal(OrgA, link.OrganisationId);
        }
    }

    [Fact]
    public async Task Erneuern_ersetzt_Hash_und_Chiffretext_derselben_Art()
    {
        var freizeit = await AnlegenAsync();
        var vorher = await LinksAsync(freizeit);

        _ = await SendenAsync(new AnmeldelinkErneuernCommand(freizeit, LinkArt.Teilnehmende), OrgA);

        var nachher = await LinksAsync(freizeit);
        Assert.NotEqual(vorher[0].TokenHash, nachher[0].TokenHash);
        Assert.NotEqual(await TokenAsync(vorher[0]), await TokenAsync(nachher[0]));
        Assert.Equal(vorher[1].TokenHash, nachher[1].TokenHash);
        Assert.Equal(vorher[0].Id, nachher[0].Id);
    }

    [Fact]
    public async Task Mandantenfilter_Anmeldelink_nur_im_eigenen_Kontext()
    {
        var freizeit = await AnlegenAsync();

        Assert.Equal(2, (await LesenAsync(db => db.Anmeldelinks.Where(l => l.FreizeitId == freizeit).ToListAsync(Abbruch), OrgA)).Count);
        foreach (var kontext in new Guid?[] { OrgB, null })
        {
            Assert.Empty(await LesenAsync(db => db.Anmeldelinks.ToListAsync(Abbruch), kontext));
        }
    }

    // ---------- Ausgebucht über HTTP ----------

    [Fact]
    public async Task Ausgebucht_steht_in_API_und_Seite_fuer_Teilnehmende_nicht_fuer_Mitarbeitende_und_nie_ohne_Hoechstzahl()
    {
        var freizeit = await AnlegenAsync();
        var links = await LinksAsync(freizeit);
        var teilnehmende = await TokenAsync(links[0]);
        var mitarbeitende = await TokenAsync(links[1]);
        using var client = _factory.CreateClient();
        _bestaetigt.Anzahl = 40;

        // Ohne Höchstzahl nie ausgebucht.
        Assert.False(await AusgebuchtAsync(client, teilnehmende));

        await HoechstzahlAsync(freizeit, "40");
        Assert.True(await AusgebuchtAsync(client, teilnehmende));
        Assert.False(await AusgebuchtAsync(client, mitarbeitende));
        var seite = await SeiteAsync(client, teilnehmende);
        Assert.Contains("Ausgebucht", seite, StringComparison.Ordinal);
        Assert.Contains("Anmelden ist trotzdem möglich, eine Zusage gibt es erst, wenn ein Platz frei wird.", seite, StringComparison.Ordinal);
        Assert.Contains("elkw-status--offen", seite, StringComparison.Ordinal);
        var seiteMitarbeitende = await SeiteAsync(client, mitarbeitende);
        Assert.DoesNotContain("Ausgebucht", seiteMitarbeitende, StringComparison.Ordinal);

        _bestaetigt.Anzahl = 39;
        Assert.False(await AusgebuchtAsync(client, teilnehmende));
        Assert.DoesNotContain("Ausgebucht", await SeiteAsync(client, teilnehmende), StringComparison.Ordinal);
    }

    // ---------- Hilfen ----------

    private sealed class FesteBestaetigte : IBestaetigteTeilnehmende
    {
        public int Anzahl { get; set; }

        public Task<int> AnzahlAsync(Guid freizeitId, CancellationToken abbruch = default) => Task.FromResult(Anzahl);
    }

    private static async Task<bool> AusgebuchtAsync(HttpClient client, string token)
    {
        using var antwort = await client.GetAsync($"/api/freizeiten/oeffentlich/{token}", Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return (await antwort.Content.ReadFromJsonAsync<JsonElement>(Abbruch)).GetProperty("ausgebucht").GetBoolean();
    }

    private static async Task<string> SeiteAsync(HttpClient client, string token)
    {
        using var anfrage = new HttpRequestMessage(HttpMethod.Get, $"/freizeit/{token}");
        anfrage.Headers.Add(ZeltlotseApiFactory.TestAbsenderIp, "203.0.113.9");
        using var antwort = await client.SendAsync(anfrage, Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return WebUtility.HtmlDecode(await antwort.Content.ReadAsStringAsync(Abbruch));
    }

    private async Task HoechstzahlAsync(Guid freizeit, string wert)
    {
        var version = await LesenAsync(db => db.Freizeiten.Where(f => f.Id == freizeit).Select(f => f.Version).SingleAsync(Abbruch), OrgA);
        var ergebnis = await SendenAsync(new FreizeitseiteFeldAendernCommand(freizeit, ApiFeld.HoechstzahlTeilnehmende, version, null, wert), OrgA);
        Assert.IsType<FreizeitseiteAenderung.Gespeichert>(ergebnis);
    }

    private Task<Guid> AnlegenAsync() =>
        SendenAsync(new FreizeitAnlegenCommand(OrgA, "Zeltlager", new DateOnly(2027, 8, 2), new DateOnly(2027, 8, 13), Andrea), OrgA);

    private Task<List<Anmeldelink>> LinksAsync(Guid freizeit) =>
        LesenAsync(db => db.Anmeldelinks.Where(l => l.FreizeitId == freizeit).OrderBy(l => l.Art).ToListAsync(Abbruch), OrgA);

    private async Task<string> TokenAsync(Anmeldelink link)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return scope.ServiceProvider.GetRequiredService<ILinkSchutz>().Lesen(link.TokenGeschuetzt, link.FreizeitId, Anmeldelink.Zweck(link.Art));
    }

    private async Task<T> SendenAsync<T>(ICommand<T> command, Guid? organisationId = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisationId);
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, Abbruch);
    }

    private async Task<T> LesenAsync<T>(Func<FreizeitenLeseDbContext, Task<T>> abfrage, Guid? organisationId = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisationId);
        return await abfrage(scope.ServiceProvider.GetRequiredService<FreizeitenLeseDbContext>());
    }

    private async Task AusfuehrenAsync(string sql, params (string Name, object Wert)[] parameter)
    {
        await using var verbindung = new Npgsql.NpgsqlConnection(_verbindung);
        await verbindung.OpenAsync(Abbruch);
        await using var befehl = new Npgsql.NpgsqlCommand(sql, verbindung);
        foreach (var (name, wert) in parameter)
        {
            befehl.Parameters.AddWithValue(name, wert);
        }

        await befehl.ExecuteNonQueryAsync(Abbruch);
    }
}
