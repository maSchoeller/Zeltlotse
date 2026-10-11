using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Demo;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Matrix-Zeilen „Demo an/aus“ und „Demo-Katalog“ (NFR-20, Konvention „Anwender-Doku“): Der Schalter
/// <c>Zeltlotse:DemoDaten:Aktiv</c> ist nur in <c>Development</c> und <c>Testing</c> erlaubt; ist er an, spielt der Host
/// nach den Migrationen die Demo-Daten ein, auch wiederholt ohne Schaden.
/// </summary>
public sealed class DemoDatenTests(PostgresContainer postgres)
{
    private const string Schalter = "Zeltlotse:DemoDaten:Aktiv";

    [Theory]
    [InlineData("Testing")]
    [InlineData("Development")]
    public async Task Demo_an_in_Testing_oder_Development_startet_und_spielt_die_Demo_Daten_ein(string umgebung)
    {
        await using var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync()) { Umgebung = umgebung };
        factory.Einstellungen[Schalter] = "true";
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health", Abbruch)).StatusCode);
        var demo = factory.Services.GetRequiredService<DemoDaten>();
        Assert.True(demo.Aktiv);
        Assert.Equal(1, demo.Einspielungen);
        Assert.Contains(ModulMigrationen.Betrieb, factory.Services.GetRequiredService<ModulMigrationen>().AngewendeteReihenfolge);
    }

    [Fact]
    public async Task Zweiter_Start_mit_Demo_Daten_gegen_dieselbe_Datenbank_startet_ebenfalls()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using (var erster = new ZeltlotseApiFactory(verbindung))
        {
            erster.Einstellungen[Schalter] = "true";
            using var client = erster.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health", Abbruch)).StatusCode);
        }

        await using var zweiter = new ZeltlotseApiFactory(verbindung);
        zweiter.Einstellungen[Schalter] = "true";
        using var zweiterClient = zweiter.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await zweiterClient.GetAsync("/health", Abbruch)).StatusCode);
        // Ab Story 1-1 prüft dieser Test hier die Demo-Zeilen in der Datenbank (einmal angelegt, nicht doppelt).
    }

    [Fact]
    public async Task Development_schaltet_die_Demo_Daten_ueber_appsettings_Development_ein()
    {
        await using var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync()) { Umgebung = "Development" };
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health", Abbruch)).StatusCode);
        var demo = factory.Services.GetRequiredService<DemoDaten>();
        Assert.True(demo.Aktiv);
        Assert.Equal(1, demo.Einspielungen);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Demo_an_ausserhalb_von_Development_und_Testing_bricht_den_Start_ab_und_nennt_den_Schalter(string umgebung)
    {
        var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync()) { Umgebung = umgebung };
        factory.Einstellungen[Schalter] = "true";

        var fehler = await StartScheitertAsync(factory);

        Assert.Contains(Schalter, fehler, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Testing", null)]
    [InlineData("Testing", "false")]
    [InlineData("Production", null)]
    [InlineData("Production", "false")]
    public async Task Demo_aus_startet_ohne_Einspielen(string umgebung, string? wert)
    {
        await using var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync()) { Umgebung = umgebung };
        if (wert is not null)
        {
            factory.Einstellungen[Schalter] = wert;
        }

        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health", Abbruch)).StatusCode);
        var demo = factory.Services.GetRequiredService<DemoDaten>();
        Assert.False(demo.Aktiv);
        Assert.Equal(0, demo.Einspielungen);
    }

    [Fact]
    public void Demo_Katalog_hat_erfundene_Stammdaten_mit_Mail_Adressen_unter_example_org()
    {
        var personen = DemoKatalog.Personen;

        Assert.False(string.IsNullOrWhiteSpace(DemoKatalog.Organisation.Name));
        Assert.False(string.IsNullOrWhiteSpace(DemoKatalog.Freizeit.Name));
        Assert.False(string.IsNullOrWhiteSpace(DemoKatalog.Freizeit.Ort));
        Assert.True(DemoKatalog.Freizeit.Beginn < DemoKatalog.Freizeit.Ende);

        Assert.Equal(2, personen.Count(p => p.Rolle == DemoRolle.Leitung));
        Assert.Equal(3, personen.Count(p => p.Rolle == DemoRolle.Team));
        Assert.Equal(2, personen.Count(p => p.Rolle == DemoRolle.Eltern));
        Assert.Equal(3, personen.Count(p => p.Rolle == DemoRolle.Kind));
        Assert.Single(personen, p => p.Rolle == DemoRolle.OrgAdmin);

        // Alle Erwachsenen haben eine Mail-Adresse, Kinder unter 18 keine (sie haben kein eigenes Konto).
        Assert.All(personen.Where(p => p.Rolle != DemoRolle.Kind), p => Assert.NotNull(p.Mail));
        Assert.All(personen.Where(p => p.Rolle == DemoRolle.Kind), p => Assert.Null(p.Mail));
        var mails = personen.Select(p => p.Mail).OfType<string>().ToList();
        Assert.All(mails, m => Assert.EndsWith("@example.org", m, StringComparison.Ordinal));
        Assert.Equal(mails.Count, mails.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // Jedes Kind gehört zur Familie eines Elternteils aus dem Katalog.
        var familien = personen.Where(p => p.Rolle == DemoRolle.Eltern).Select(p => p.Familie).ToHashSet();
        Assert.All(personen.Where(p => p.Rolle == DemoRolle.Kind), k => Assert.Contains(k.Familie, familien));
        Assert.All(personen, p => Assert.False(string.IsNullOrWhiteSpace(p.Vorname + p.Nachname)));
    }
}
