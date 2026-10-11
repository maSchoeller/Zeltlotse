using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>Matrix-Zeile „Hauptschlüssel fehlt/ungültig“ und die Startprüfungen aus AD-22.</summary>
public sealed class StartpruefungTests(PostgresContainer postgres)
{
    [Theory]
    [InlineData("")]
    [InlineData("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHg==")] // 31 Bytes
    [InlineData("kein base64")]
    public async Task Fehlender_oder_ungueltiger_Hauptschluessel_bricht_den_Start_ab_ohne_den_Wert_zu_nennen(string wert)
    {
        var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync());
        factory.Einstellungen["Zeltlotse:Verschluesselung:Hauptschluessel"] = wert;

        var fehler = await StartScheitertAsync(factory);

        Assert.Contains("Zeltlotse:Verschluesselung:Hauptschluessel", fehler, StringComparison.Ordinal);
        if (wert.Length > 0)
        {
            Assert.DoesNotContain(wert, fehler, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("a3Vyeg==")]
    public async Task Fehlender_oder_zu_kurzer_HMAC_Schluessel_bricht_den_Start_ab(string wert)
    {
        var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync());
        factory.Einstellungen["Zeltlotse:Protokoll:HmacSchluessel"] = wert;

        var fehler = await StartScheitertAsync(factory);

        Assert.Contains("Zeltlotse:Protokoll:HmacSchluessel", fehler, StringComparison.Ordinal);
        if (wert.Length > 0)
        {
            Assert.DoesNotContain(wert, fehler, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("keine-mail")]
    [InlineData("Gerd <gerd.betreiber@example.org>")]
    [InlineData("")]
    public async Task Ungueltige_Mail_in_GlobalAdmins_bricht_den_Start_ab_und_nennt_den_Schluessel(string wert)
    {
        var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync());
        factory.Einstellungen["Zeltlotse:GlobalAdmins:0"] = "gerd.betreiber@example.org";
        factory.Einstellungen["Zeltlotse:GlobalAdmins:1"] = wert;

        var fehler = await StartScheitertAsync(factory);

        Assert.Contains("Zeltlotse:GlobalAdmins", fehler, StringComparison.Ordinal);
        Assert.DoesNotContain("gerd.betreiber", fehler, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GlobalAdmins_als_Einzelwert_statt_Liste_bricht_den_Start_ab_und_nennt_den_Schluessel()
    {
        var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync());
        factory.Einstellungen["Zeltlotse:GlobalAdmins"] = "gerd.betreiber@example.org";

        var fehler = await StartScheitertAsync(factory);

        Assert.Contains("Zeltlotse:GlobalAdmins", fehler, StringComparison.Ordinal);
        Assert.DoesNotContain("gerd.betreiber", fehler, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GlobalAdmins_aus_der_Konfiguration_bekommen_beim_Start_Konto_und_Kennzeichen_ohne_Liste_niemand()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using (var mitListe = new ZeltlotseApiFactory(verbindung))
        {
            mitListe.Einstellungen["Zeltlotse:GlobalAdmins:0"] = " Gerd.Betreiber@example.org ";
            _ = mitListe.CreateClient();
        }

        Assert.True(await AnmeldeHelfer.WertAsync<bool>(verbindung, "SELECT \"GlobalAdmin\" FROM konten.konto WHERE \"Mail\" = 'gerd.betreiber@example.org'"));

        await using var ohneListe = new ZeltlotseApiFactory(verbindung);
        _ = ohneListe.CreateClient();

        Assert.Equal(0L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM konten.konto WHERE \"GlobalAdmin\""));
        Assert.Equal(1L, await AnmeldeHelfer.WertAsync<long>(verbindung, "SELECT count(*) FROM konten.konto"));
    }

    [Fact]
    public async Task EnableSensitiveDataLogging_bricht_ausserhalb_von_Testing_den_Start_ab()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        var factory = new ZeltlotseApiFactory(verbindung) { Umgebung = "Production" };
        factory.ZusatzDienste.Add(s => s.AddDbContext<GeschwaetzigerKontext>(o => o.UseNpgsql(verbindung).EnableSensitiveDataLogging()));

        var fehler = await StartScheitertAsync(factory);

        Assert.Contains(nameof(GeschwaetzigerKontext), fehler, StringComparison.Ordinal);
        Assert.Contains("EnableSensitiveDataLogging", fehler, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnableDetailedErrors_bricht_ausserhalb_von_Testing_den_Start_ab()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        var factory = new ZeltlotseApiFactory(verbindung) { Umgebung = "Production" };
        factory.ZusatzDienste.Add(s => s.AddDbContext<GeschwaetzigerKontext>(o => o.UseNpgsql(verbindung).EnableDetailedErrors()));

        var fehler = await StartScheitertAsync(factory);

        Assert.Contains("EnableDetailedErrors", fehler, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Include_Error_Detail_in_der_Verbindung_bricht_ausserhalb_von_Testing_den_Start_ab()
    {
        var verbindung = new NpgsqlConnectionStringBuilder(await postgres.NeueDatenbankAsync()) { IncludeErrorDetail = true }.ConnectionString;
        var factory = new ZeltlotseApiFactory(verbindung) { Umgebung = "Production" };

        var fehler = await StartScheitertAsync(factory);

        Assert.Contains("Include Error Detail", fehler, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", fehler, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task In_Testing_sind_Sensitive_Logging_und_Error_Detail_erlaubt()
    {
        var verbindung = new NpgsqlConnectionStringBuilder(await postgres.NeueDatenbankAsync()) { IncludeErrorDetail = true }.ConnectionString;
        await using var factory = new ZeltlotseApiFactory(verbindung);
        factory.ZusatzDienste.Add(s => s.AddDbContext<GeschwaetzigerKontext>(o => o.UseNpgsql(verbindung).EnableSensitiveDataLogging()));
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health", Abbruch)).StatusCode);
    }

    [Fact]
    public async Task Ohne_Verstoss_startet_der_Host_auch_ausserhalb_von_Testing()
    {
        await using var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync()) { Umgebung = "Production" };
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health", Abbruch)).StatusCode);
    }

    private sealed class GeschwaetzigerKontext(DbContextOptions<GeschwaetzigerKontext> optionen) : DbContext(optionen);
}
