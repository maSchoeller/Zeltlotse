using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry.Logs;
using Zeltlotse.Api.Adapter;
using Zeltlotse.Api.Contracts.Konten;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 1-1, Matrix-Zeile „Mail-Konfiguration“ und Mail-Adapter (AD-13, AD-22, AD-23): Startprüfung, Text der
/// Login-Mail, SMTP-Adapter gegen Mailpit und keine Mail-Adressen, Token oder Codes in Logs.
/// </summary>
public sealed partial class MailTests(PostgresContainer postgres, MailpitContainer mailpit) : IClassFixture<MailpitContainer>
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Ordner_Adapter_ausserhalb_von_Development_und_Testing_bricht_den_Start_ab(string umgebung)
    {
        var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync()) { Umgebung = umgebung };
        factory.Einstellungen["Zeltlotse:Mail:Art"] = "Ordner";
        factory.Einstellungen["Zeltlotse:Mail:Ordner"] = Path.GetTempPath();

        var fehler = await StartScheitertAsync(factory);

        Assert.Contains("Zeltlotse:Mail:Art", fehler, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Zeltlotse:Mail:Art", "")]
    [InlineData("Zeltlotse:Mail:Art", "Brieftaube")]
    [InlineData("Zeltlotse:Mail:Ordner", "")]
    [InlineData("Zeltlotse:OeffentlicheAdresse", "")]
    [InlineData("Zeltlotse:OeffentlicheAdresse", "/nur/ein/pfad")]
    [InlineData("Zeltlotse:OeffentlicheAdresse", "ftp://lager.example")]
    public async Task Fehlende_oder_ungueltige_Konfiguration_bricht_den_Start_ab_und_nennt_den_Schluessel(string schluessel, string wert)
    {
        var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync());
        factory.Einstellungen[schluessel] = wert;

        var fehler = await StartScheitertAsync(factory);

        Assert.Contains(schluessel, fehler, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Http_als_oeffentliche_Adresse_bricht_ausserhalb_von_Development_und_Testing_den_Start_ab(string umgebung)
    {
        var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync()) { Umgebung = umgebung };
        factory.Einstellungen["Zeltlotse:OeffentlicheAdresse"] = "http://lager.example";

        var fehler = await StartScheitertAsync(factory);

        Assert.Contains("Zeltlotse:OeffentlicheAdresse", fehler, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Http_als_oeffentliche_Adresse_ist_in_Testing_erlaubt()
    {
        await using var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync());
        factory.Einstellungen["Zeltlotse:OeffentlicheAdresse"] = "http://127.0.0.1:5000";
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health", Abbruch)).StatusCode);
    }

    [Theory]
    [InlineData("Zeltlotse:Mail:Smtp:Host")]
    [InlineData("Zeltlotse:Mail:Absender")]
    public async Task Smtp_ohne_Host_oder_Absender_bricht_den_Start_ab(string schluessel)
    {
        var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync()) { Umgebung = "Production" };
        factory.Einstellungen[schluessel] = "";

        var fehler = await StartScheitertAsync(factory);

        Assert.Contains(schluessel, fehler, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Smtp_mit_Host_und_Absender_startet_auch_in_Production()
    {
        await using var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync()) { Umgebung = "Production" };
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health", Abbruch)).StatusCode);
    }

    [Fact]
    public async Task Login_Mail_ist_deutsch_ohne_Anrede_mit_Link_auf_die_oeffentliche_Adresse_und_Code()
    {
        await using var factory = Factory(await postgres.NeueDatenbankAsync());
        factory.Einstellungen["Zeltlotse:OeffentlicheAdresse"] = "https://lager.example/";
        using var client = factory.CreateClient();

        await LinkAnfordernAsync(client, "paul@example.org");

        var mail = factory.Mails.LetzteAn("paul@example.org");
        Assert.Equal("Anmelden bei Zeltlotse", mail.Betreff);
        Assert.Matches("^https://lager\\.example/app/anmelden/link/[A-Za-z0-9_-]{43}$", mail.Link);
        Assert.Contains($"Login-Code: {mail.Code}", mail.Text, StringComparison.Ordinal);
        Assert.Contains("15 Minuten", mail.Text, StringComparison.Ordinal);
        Assert.DoesNotMatch(Anrede(), mail.Text);
    }

    [Fact]
    public async Task Smtp_Adapter_schickt_die_Mail_an_den_Mail_Server()
    {
        var empfaenger = $"test-{Guid.NewGuid():N}@example.org";
        var adapter = new SmtpMailVersand(Options.Create(new MailOptionen
        {
            Art = MailArt.Smtp,
            Absender = "Zeltlotse <zeltlotse@example.org>",
            Smtp = new SmtpOptionen { Host = mailpit.Host, Port = mailpit.Smtp, Verschluesselung = MailKit.Security.SecureSocketOptions.None },
        }));

        await adapter.SendenAsync(empfaenger, "Anmelden bei Zeltlotse", "Grüße aus dem Zeltlager: äöüß", Abbruch);

        using var http = new HttpClient { BaseAddress = mailpit.Web };
        var suche = await http.GetFromJsonAsync<JsonElement>($"/api/v1/search?query=to:{Uri.EscapeDataString(empfaenger)}", Abbruch);
        var nachricht = Assert.Single(suche.GetProperty("messages").EnumerateArray());
        Assert.Equal("Anmelden bei Zeltlotse", nachricht.GetProperty("Subject").GetString());
        Assert.Equal("zeltlotse@example.org", nachricht.GetProperty("From").GetProperty("Address").GetString());
        var voll = await http.GetFromJsonAsync<JsonElement>($"/api/v1/message/{nachricht.GetProperty("ID").GetString()}", Abbruch);
        Assert.Contains("Grüße aus dem Zeltlager: äöüß", voll.GetProperty("Text").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mail_Adressen_Token_und_Codes_stehen_nicht_in_Logs_oder_im_Sicherheitsprotokoll()
    {
        const string mail = "geheim.person@example.org";
        var eintraege = new List<LogRecord>();
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = Factory(verbindung);
        factory.ZusatzDienste.Add(s => s.Configure<OpenTelemetryLoggerOptions>(o => o.AddInMemoryExporter(eintraege)));
        using var client = factory.CreateClient();

        var anfrageId = await LinkAnfordernAsync(client, mail);
        var gesendet = factory.Mails.LetzteAn(mail);
        using (var falsch = await PostAsync(client, "/api/konten/login-code/einloesen", new LoginCodeEinloesen(anfrageId, "12345x")))
        {
            Assert.Equal(HttpStatusCode.NotFound, falsch.StatusCode);
        }

        using (var link = await PostAsync(client, "/api/konten/login-link/einloesen", new LoginLinkEinloesen(gesendet.Token)))
        {
            Assert.Equal(HttpStatusCode.NoContent, link.StatusCode);
        }

        using (var nochmal = await PostAsync(client, "/api/konten/login-link/einloesen", new LoginLinkEinloesen(gesendet.Token)))
        {
            Assert.Equal(HttpStatusCode.NotFound, nochmal.StatusCode);
        }

        List<LogRecord> exportiert;
        lock (eintraege)
        {
            exportiert = [.. eintraege];
        }

        Assert.NotEmpty(exportiert);
        foreach (var geheimnis in new[] { mail, gesendet.Token, gesendet.Code })
        {
            foreach (var eintrag in exportiert)
            {
                Assert.DoesNotContain(geheimnis, eintrag.FormattedMessage ?? "", StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(eintrag.Attributes ?? [], a => a.Value?.ToString()?.Contains(geheimnis, StringComparison.OrdinalIgnoreCase) == true);
            }
        }

        // Das Sicherheitsprotokoll hat ohnehin nur Kennungen (keine Text-Spalte, siehe SicherheitsprotokollTests).
        Assert.True(await WertAsync<long>(verbindung, "SELECT count(*) FROM betrieb.sicherheitsprotokoll") >= 3);
    }

    [GeneratedRegex(@"\b(?:[Dd]u|[Dd]ich|[Dd]ir|[Dd]ein(?:e[mnrs]?)?|[Ee]uch|[Ee]uer|[Ee]ure[mnrs]?|Ihnen|Sie|Ihr(?:e[mnrs]?)?)\b")]
    private static partial Regex Anrede();
}
