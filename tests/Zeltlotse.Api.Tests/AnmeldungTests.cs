using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zeltlotse.Api.Contracts.Konten;
using Zeltlotse.SharedKernel.Mail;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 1-1, Matrix-Zeilen der Anmeldung (AD-11, AD-12, AD-25, AD-30 „Token“): Login-Link und Login-Code per Mail,
/// Passwort, Abmelden, Sitzungsstempel und Zugriffsbegrenzung, gegen echtes PostgreSQL.
/// </summary>
public sealed class AnmeldungTests(PostgresContainer postgres)
{
    private const string Bekannt = "jonas.huettner@example.org";
    private const string Unbekannt = "neu@example.org";

    [Fact]
    public async Task Link_anfordern_antwortet_fuer_bekannte_und_unbekannte_Mail_gleich_und_schickt_je_genau_eine_Mail()
    {
        await using var factory = Factory(await postgres.NeueDatenbankAsync());
        using var client = factory.CreateClient();
        await AnmeldenAsync(factory, client, Bekannt);
        using var anonym = factory.CreateClient();
        var vorher = factory.Mails.An(Bekannt).Count;

        using var bekannt = await PostAsync(anonym, "/api/konten/login-link", new LoginLinkAnfordern("  Jonas.Huettner@Example.org "));
        using var unbekannt = await PostAsync(anonym, "/api/konten/login-link", new LoginLinkAnfordern(Unbekannt));

        foreach (var antwort in new[] { bekannt, unbekannt })
        {
            Assert.Equal(HttpStatusCode.Accepted, antwort.StatusCode);
            var json = await JsonAsync(antwort);
            Assert.Equal(["anfrageId"], Namen(json));
            Assert.NotEqual(Guid.Empty, json.GetProperty("anfrageId").GetGuid());
        }

        Assert.Equal(vorher + 1, factory.Mails.An(Bekannt).Count);
        var mail = Assert.Single(factory.Mails.An(Unbekannt));
        Assert.Equal("Anmelden bei Zeltlotse", mail.Betreff);
        Assert.StartsWith("https://localhost/app/anmelden/link/", mail.Link, StringComparison.Ordinal);
        Assert.Equal(43, mail.Token.Length);
        Assert.Matches("^[0-9]{6}$", mail.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("keine-mail")]
    [InlineData("zwei @example.org")]
    [InlineData("x<other@y.de>")]
    [InlineData("Paul <paul@example.org>")]
    [InlineData("\"a,b\"@x.de")]
    [InlineData("a@x.de, b@y.de")]
    [InlineData("a@x.de;b@y.de")]
    public async Task Ungueltige_Mail_ergibt_400_am_Feld_mail_ohne_Anfrage_und_ohne_Mail(string mail)
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = Factory(verbindung);
        using var client = factory.CreateClient();

        using var antwort = await PostAsync(client, "/api/konten/login-link", new LoginLinkAnfordern(mail));

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        var problem = await ProblemAsync(antwort);
        Assert.Equal("validierung", Code(problem));
        Assert.Equal(["mail"], Namen(problem.GetProperty("errors")));
        // Validierung vor dem Handler (AD-27): nichts angelegt, nichts verschickt.
        Assert.Equal(0L, await WertAsync<long>(verbindung, "SELECT count(*) FROM konten.login_anfrage"));
        Assert.Empty(factory.Mails.Alle());
    }

    [Fact]
    public async Task Link_einloesen_legt_fuer_eine_unbekannte_Mail_das_Konto_an_und_setzt_das_Sitzungs_Cookie()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = Factory(verbindung);
        using var client = factory.CreateClient();
        await LinkAnfordernAsync(client, Unbekannt);
        var token = factory.Mails.LetzteAn(Unbekannt).Token;
        Assert.Equal(0L, await WertAsync<long>(verbindung, "SELECT count(*) FROM konten.konto"));

        using var antwort = await PostAsync(client, "/api/konten/login-link/einloesen", new LoginLinkEinloesen(token));

        Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);
        var cookie = Assert.Single(antwort.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-Zeltlotse=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        // Gleitend 30 Tage (AD-12): Ablauf des dauerhaften Cookies = Uhr + 30 Tage.
        var ablauf = System.Text.RegularExpressions.Regex.Match(cookie, "expires=([^;]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        Assert.True(ablauf.Success, cookie);
        var bis = DateTimeOffset.ParseExact(ablauf.Groups[1].Value.Trim(), "R", System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(bis, factory.Uhr.GetUtcNow().AddDays(30).AddMinutes(-1), factory.Uhr.GetUtcNow().AddDays(30).AddMinutes(1));
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(1L, await WertAsync<long>(verbindung, "SELECT count(*) FROM konten.konto WHERE \"Mail\" = @mail", ("mail", Unbekannt)));
        Assert.NotEqual(Guid.Empty, await PersonIdAsync(verbindung, Unbekannt));
        using var konto = await client.GetAsync("/api/ich/konto", Abbruch);
        Assert.Equal(HttpStatusCode.OK, konto.StatusCode);
        var json = await JsonAsync(konto);
        Assert.Equal(["hatPasswort", "mail"], Namen(json));
        Assert.Equal(Unbekannt, json.GetProperty("mail").GetString());
        Assert.False(json.GetProperty("hatPasswort").GetBoolean());
        Assert.Equal(1L, await ProtokollAsync(verbindung, SicherheitsEreignisArt.Anmeldung, await PersonIdAsync(verbindung, Unbekannt)));
    }

    [Fact]
    public async Task Link_einloesen_fuer_ein_bestehendes_Konto_gibt_eine_Sitzung_mit_derselben_PersonId()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = Factory(verbindung);
        using var erster = await AngemeldeterClientAsync(factory, Bekannt);
        var personId = await PersonIdAsync(verbindung, Bekannt);

        using var zweiter = await AngemeldeterClientAsync(factory, "JONAS.huettner@example.org");

        Assert.Equal(1L, await WertAsync<long>(verbindung, "SELECT count(*) FROM konten.konto"));
        Assert.Equal(personId, await PersonIdAsync(verbindung, Bekannt));
        Assert.Equal(2L, await ProtokollAsync(verbindung, SicherheitsEreignisArt.Anmeldung, personId));
        using var konto = await zweiter.GetAsync("/api/ich/konto", Abbruch);
        Assert.Equal(Bekannt, (await JsonAsync(konto)).GetProperty("mail").GetString());
    }

    public static TheoryData<string> TokenFaelle => ["abgelaufen", "zweites Einlösen", "durch neue Anfrage verworfen", "anderer Zweck", "unbekannt", "kein Token"];

    [Theory]
    [MemberData(nameof(TokenFaelle))]
    public async Task Ungueltiges_Token_ergibt_404_und_einen_Eintrag_im_Sicherheitsprotokoll(string fall)
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = Factory(verbindung);
        using var client = factory.CreateClient();
        var anfrageId = await LinkAnfordernAsync(client, Unbekannt);
        var token = factory.Mails.LetzteAn(Unbekannt).Token;
        switch (fall)
        {
            case "abgelaufen":
                factory.Uhr.Advance(TimeSpan.FromMinutes(16));
                break;
            case "zweites Einlösen":
                using (var erstes = await PostAsync(factory.CreateClient(), "/api/konten/login-link/einloesen", new LoginLinkEinloesen(token)))
                {
                    Assert.Equal(HttpStatusCode.NoContent, erstes.StatusCode);
                }

                break;
            case "durch neue Anfrage verworfen":
                await LinkAnfordernAsync(client, Unbekannt);
                break;
            case "anderer Zweck":
                // Dasselbe Token, aber mit einem anderen Zweck gehasht (etwa eine Einladung): passt nie zum Login.
                await AusfuehrenAsync(
                    verbindung,
                    "UPDATE konten.login_anfrage SET \"TokenHash\" = sha256(convert_to('einladung:' || @token, 'UTF8')) WHERE \"Id\" = @id",
                    ("token", token),
                    ("id", anfrageId));
                break;
            case "unbekannt":
                token = new string('A', 43);
                break;
            case "kein Token":
                token = "";
                break;
        }

        using var antwort = await PostAsync(client, "/api/konten/login-link/einloesen", new LoginLinkEinloesen(token));

        Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
        Assert.Equal("application/problem+json", antwort.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(antwort.Headers, h => h.Key == "Set-Cookie" && h.Value.Any(v => v.StartsWith("__Host-Zeltlotse=", StringComparison.Ordinal)));
        Assert.True(await ProtokollAsync(verbindung, SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen) >= 1);
        using var konto = await client.GetAsync("/api/ich/konto", Abbruch);
        Assert.Equal(HttpStatusCode.Unauthorized, konto.StatusCode);
    }

    [Fact]
    public async Task Gueltig_bis_knapp_vor_15_Minuten()
    {
        await using var factory = Factory(await postgres.NeueDatenbankAsync());
        using var client = factory.CreateClient();
        await LinkAnfordernAsync(client, Unbekannt);
        factory.Uhr.Advance(TimeSpan.FromMinutes(15) - TimeSpan.FromSeconds(1));

        using var antwort = await PostAsync(client, "/api/konten/login-link/einloesen", new LoginLinkEinloesen(factory.Mails.LetzteAn(Unbekannt).Token));

        Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);
    }

    [Fact]
    public async Task Das_Token_einer_Anfrage_fuer_Mail_A_ergibt_immer_eine_Sitzung_von_Konto_A()
    {
        const string a = "a@example.org";
        const string b = "b@example.org";
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = Factory(verbindung);
        using var kontoB = await AngemeldeterClientAsync(factory, b);
        using var client = await AngemeldeterClientAsync(factory, b);
        await LinkAnfordernAsync(client, a);

        // Derselbe Client, bisher als B angemeldet, löst den Link für A ein.
        using var antwort = await PostAsync(client, "/api/konten/login-link/einloesen", new LoginLinkEinloesen(factory.Mails.LetzteAn(a).Token));

        Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);
        using var konto = await client.GetAsync("/api/ich/konto", Abbruch);
        Assert.Equal(a, (await JsonAsync(konto)).GetProperty("mail").GetString());
        Assert.NotEqual(await PersonIdAsync(verbindung, a), await PersonIdAsync(verbindung, b));
    }

    [Fact]
    public async Task Code_richtig_meldet_an()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = Factory(verbindung);
        using var client = factory.CreateClient();
        var anfrageId = await LinkAnfordernAsync(client, Unbekannt);

        using var antwort = await PostAsync(client, "/api/konten/login-code/einloesen", new LoginCodeEinloesen(anfrageId, factory.Mails.LetzteAn(Unbekannt).Code));

        Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);
        using var konto = await client.GetAsync("/api/ich/konto", Abbruch);
        Assert.Equal(HttpStatusCode.OK, konto.StatusCode);
        Assert.Equal(1L, await ProtokollAsync(verbindung, SicherheitsEreignisArt.Anmeldung, await PersonIdAsync(verbindung, Unbekannt)));

        // Link und Code lösen dieselbe Anfrage ein, nur einmal.
        using var link = await PostAsync(factory.CreateClient(), "/api/konten/login-link/einloesen", new LoginLinkEinloesen(factory.Mails.LetzteAn(Unbekannt).Token));
        Assert.Equal(HttpStatusCode.NotFound, link.StatusCode);
    }

    [Fact]
    public async Task Code_falsch_ergibt_404_und_nach_fuenf_falschen_ist_auch_der_richtige_Code_ungueltig()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = Factory(verbindung);
        using var client = factory.CreateClient();
        var anfrageId = await LinkAnfordernAsync(client, Unbekannt);
        var code = factory.Mails.LetzteAn(Unbekannt).Code;
        var falsch = code == "000000" ? "000001" : "000000";

        for (var versuch = 1; versuch <= 5; versuch++)
        {
            using var antwort = await PostAsync(client, "/api/konten/login-code/einloesen", new LoginCodeEinloesen(anfrageId, falsch));
            Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
        }

        using var sechster = await PostAsync(client, "/api/konten/login-code/einloesen", new LoginCodeEinloesen(anfrageId, code));

        Assert.Equal(HttpStatusCode.NotFound, sechster.StatusCode);
        Assert.Equal(6L, await ProtokollAsync(verbindung, SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen));
        Assert.Equal(0L, await WertAsync<long>(verbindung, "SELECT count(*) FROM konten.konto"));
    }

    [Fact]
    public async Task Code_nach_vier_falschen_gilt_noch_und_abgelaufen_oder_fremde_Anfrage_nicht()
    {
        await using var factory = Factory(await postgres.NeueDatenbankAsync());
        using var client = factory.CreateClient();
        var anfrageId = await LinkAnfordernAsync(client, Unbekannt);
        var code = factory.Mails.LetzteAn(Unbekannt).Code;
        var falsch = code == "000000" ? "000001" : "000000";
        for (var versuch = 1; versuch <= 4; versuch++)
        {
            using var antwort = await PostAsync(client, "/api/konten/login-code/einloesen", new LoginCodeEinloesen(anfrageId, falsch));
            Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
        }

        using var fremd = await PostAsync(client, "/api/konten/login-code/einloesen", new LoginCodeEinloesen(Guid.CreateVersion7(), code));
        Assert.Equal(HttpStatusCode.NotFound, fremd.StatusCode);

        using var richtig = await PostAsync(client, "/api/konten/login-code/einloesen", new LoginCodeEinloesen(anfrageId, code));
        Assert.Equal(HttpStatusCode.NoContent, richtig.StatusCode);

        var zweite = await LinkAnfordernAsync(client, "andere@example.org");
        factory.Uhr.Advance(TimeSpan.FromMinutes(16));
        using var abgelaufen = await PostAsync(client, "/api/konten/login-code/einloesen", new LoginCodeEinloesen(zweite, factory.Mails.LetzteAn("andere@example.org").Code));
        Assert.Equal(HttpStatusCode.NotFound, abgelaufen.StatusCode);
    }

    [Fact]
    public async Task Passwort_festlegen_dann_mit_richtigem_Passwort_anmelden_und_falsches_oder_fehlendes_ergibt_401()
    {
        const string passwort = "Zeltlager-2027!";
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = Factory(verbindung);
        using var client = await AngemeldeterClientAsync(factory, Bekannt);
        using var ohnePasswort = await AngemeldeterClientAsync(factory, Unbekannt);

        using (var festlegen = await SendenAsync(client, HttpMethod.Put, "/api/ich/konto/passwort", new PasswortFestlegen(passwort)))
        {
            Assert.Equal(HttpStatusCode.NoContent, festlegen.StatusCode);
        }

        using (var konto = await client.GetAsync("/api/ich/konto", Abbruch))
        {
            Assert.True((await JsonAsync(konto)).GetProperty("hatPasswort").GetBoolean());
        }

        var hash = await WertAsync<string>(verbindung, "SELECT \"PasswortHash\" FROM konten.konto WHERE \"Mail\" = @mail", ("mail", Bekannt));
        Assert.DoesNotContain(passwort, hash, StringComparison.Ordinal);
        Assert.Equal(1, Convert.FromBase64String(hash)[0]); // Identity-Hashformat v3

        using var neu = factory.CreateClient();
        using var richtig = await PostAsync(neu, "/api/konten/anmelden", new PasswortAnmeldung(" Jonas.Huettner@example.org", passwort));
        Assert.Equal(HttpStatusCode.NoContent, richtig.StatusCode);
        using (var konto = await neu.GetAsync("/api/ich/konto", Abbruch))
        {
            Assert.Equal(Bekannt, (await JsonAsync(konto)).GetProperty("mail").GetString());
        }

        var titel = new List<string?>();
        foreach (var falsch in new[]
                 {
                     new PasswortAnmeldung(Bekannt, passwort + "x"),
                     new PasswortAnmeldung(Unbekannt, passwort),
                     new PasswortAnmeldung("niemand@example.org", passwort),
                     new PasswortAnmeldung(null, null),
                 })
        {
            using var anonym = factory.CreateClient();
            using var antwort = await PostAsync(anonym, "/api/konten/anmelden", falsch);
            Assert.Equal(HttpStatusCode.Unauthorized, antwort.StatusCode);
            var problem = await ProblemAsync(antwort);
            titel.Add(problem.TryGetProperty("title", out var t) ? t.GetString() : null);
            Assert.Null(Code(problem));
            using var konto = await anonym.GetAsync("/api/ich/konto", Abbruch);
            Assert.Equal(HttpStatusCode.Unauthorized, konto.StatusCode);
        }

        // Keine Konto-Aufzählung: falsches Passwort und Konto ohne Passwort antworten gleich.
        Assert.Single(titel.Distinct());
        Assert.Equal(4L, await ProtokollAsync(verbindung, SicherheitsEreignisArt.AnmeldungFehlgeschlagen));
        Assert.Equal(1L, await ProtokollAsync(verbindung, SicherheitsEreignisArt.AnmeldungFehlgeschlagen, await PersonIdAsync(verbindung, Bekannt)));
    }

    [Theory]
    [InlineData("kurz12345")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Passwort_unter_10_Zeichen_ergibt_400_am_Feld_passwort(string? passwort)
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = Factory(verbindung);
        using var client = await AngemeldeterClientAsync(factory, Bekannt);

        using var antwort = await SendenAsync(client, HttpMethod.Put, "/api/ich/konto/passwort", new PasswortFestlegen(passwort));

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.Equal(["passwort"], Namen((await ProblemAsync(antwort)).GetProperty("errors")));
        Assert.Equal(0L, await WertAsync<long>(verbindung, "SELECT count(*) FROM konten.konto WHERE \"PasswortHash\" IS NOT NULL"));
    }

    [Theory]
    [InlineData(10, HttpStatusCode.NoContent)]
    [InlineData(200, HttpStatusCode.NoContent)]
    [InlineData(201, HttpStatusCode.BadRequest)]
    public async Task Passwort_hat_10_bis_200_Zeichen(int laenge, HttpStatusCode erwartet)
    {
        await using var factory = Factory(await postgres.NeueDatenbankAsync());
        using var client = await AngemeldeterClientAsync(factory, Bekannt);

        using var antwort = await SendenAsync(client, HttpMethod.Put, "/api/ich/konto/passwort", new PasswortFestlegen(new string('p', laenge)));

        Assert.Equal(erwartet, antwort.StatusCode);
    }

    [Fact]
    public async Task Neues_Passwort_beendet_andere_Sitzungen_und_die_eigene_bleibt_gueltig()
    {
        await using var factory = Factory(await postgres.NeueDatenbankAsync());
        using var eigene = await AngemeldeterClientAsync(factory, Bekannt);
        using var andere = await AngemeldeterClientAsync(factory, Bekannt);
        using (var vorher = await andere.GetAsync("/api/ich/konto", Abbruch))
        {
            Assert.Equal(HttpStatusCode.OK, vorher.StatusCode);
        }

        using var festlegen = await SendenAsync(eigene, HttpMethod.Put, "/api/ich/konto/passwort", new PasswortFestlegen("ein-langes-Passwort"));

        Assert.Equal(HttpStatusCode.NoContent, festlegen.StatusCode);
        Assert.Contains(festlegen.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-Zeltlotse=", StringComparison.Ordinal));
        using var anders = await andere.GetAsync("/api/ich/konto", Abbruch);
        Assert.Equal(HttpStatusCode.Unauthorized, anders.StatusCode);
        Assert.Equal("application/problem+json", anders.Content.Headers.ContentType?.MediaType);
        using var eigen = await eigene.GetAsync("/api/ich/konto", Abbruch);
        Assert.Equal(HttpStatusCode.OK, eigen.StatusCode);
    }

    [Fact]
    public async Task Abmelden_loescht_das_Cookie_und_danach_ist_die_Sitzung_weg()
    {
        await using var factory = Factory(await postgres.NeueDatenbankAsync());
        using var client = await AngemeldeterClientAsync(factory, Bekannt);

        using var abmelden = await PostAsync(client, "/api/konten/abmelden");

        Assert.Equal(HttpStatusCode.NoContent, abmelden.StatusCode);
        var geloescht = Assert.Single(abmelden.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-Zeltlotse=", StringComparison.Ordinal));
        Assert.Contains("expires=Thu, 01 Jan 1970", geloescht, StringComparison.OrdinalIgnoreCase);
        using var konto = await client.GetAsync("/api/ich/konto", Abbruch);
        Assert.Equal(HttpStatusCode.Unauthorized, konto.StatusCode);
    }

    [Fact]
    public async Task Abmelden_ohne_Sitzung_ergibt_401()
    {
        await using var factory = Factory(await postgres.NeueDatenbankAsync());
        using var client = factory.CreateClient();

        using var antwort = await PostAsync(client, "/api/konten/abmelden");

        Assert.Equal(HttpStatusCode.Unauthorized, antwort.StatusCode);
    }

    [Fact]
    public async Task Gefaelschtes_oder_veraltetes_Cookie_gilt_nicht()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = Factory(verbindung);
        using var client = await AngemeldeterClientAsync(factory, Bekannt);

        // Konto weg (z. B. gelöscht): die Sitzung gilt nicht mehr.
        await AusfuehrenAsync(verbindung, "DELETE FROM konten.konto");

        using var konto = await client.GetAsync("/api/ich/konto", Abbruch);
        Assert.Equal(HttpStatusCode.Unauthorized, konto.StatusCode);

        using var fremd = factory.CreateClient();
        using var anfrage = new HttpRequestMessage(HttpMethod.Get, "/api/ich/konto");
        anfrage.Headers.Add("Cookie", "__Host-Zeltlotse=CfDJ8gefaelscht");
        using var gefaelscht = await fremd.SendAsync(anfrage, Abbruch);
        Assert.Equal(HttpStatusCode.Unauthorized, gefaelscht.StatusCode);
    }

    [Fact]
    public async Task Die_21_Anfrage_je_Minute_an_einen_Anmelde_Endpunkt_ergibt_429()
    {
        await using var factory = Factory(await postgres.NeueDatenbankAsync(), mitZugriffsbegrenzung: true);
        using var client = factory.CreateClient();
        // Antiforgery-Abruf (1) und 19 Anfragen: zusammen 20 in dieser Minute, alle erlaubt.
        var token = await AntiforgeryTokenAsync(client);
        for (var i = 0; i < 19; i++)
        {
            using var erlaubt = await MitTokenAsync(client, token, "/api/konten/anmelden", new PasswortAnmeldung("x@example.org", "falsch-falsch"));
            Assert.Equal(HttpStatusCode.Unauthorized, erlaubt.StatusCode);
        }

        using var antwort = await MitTokenAsync(client, token, "/api/konten/anmelden", new PasswortAnmeldung("x@example.org", "falsch-falsch"));

        Assert.Equal((HttpStatusCode)429, antwort.StatusCode);
        Assert.Equal("zugriffsbegrenzt", Code(await ProblemAsync(antwort)));
    }

    [Fact]
    public async Task Fehler_im_Handler_speichert_nichts_aber_der_Protokolleintrag_davor_bleibt()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = Factory(verbindung);
        using var client = factory.CreateClient();
        var anfrageId = await LinkAnfordernAsync(client, Unbekannt);
        // Der Fehlversuch lässt sich nicht speichern: Das Speichern am Ende des Handlers scheitert.
        await AusfuehrenAsync(verbindung, "ALTER TABLE konten.login_anfrage ADD CONSTRAINT kein_fehlversuch CHECK (\"Fehlversuche\" < 1)");

        using var antwort = await PostAsync(client, "/api/konten/login-code/einloesen", new LoginCodeEinloesen(anfrageId, "abcdef"));

        Assert.Equal(HttpStatusCode.InternalServerError, antwort.StatusCode);
        Assert.Equal(0, await WertAsync<int>(verbindung, "SELECT \"Fehlversuche\" FROM konten.login_anfrage WHERE \"Id\" = @id", ("id", anfrageId)));
        Assert.Equal(1L, await ProtokollAsync(verbindung, SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen));
    }

    [Fact]
    public async Task Scheitert_der_Mail_Versand_bleibt_die_Anfrage_und_die_Antwort_ist_500()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = Factory(verbindung);
        factory.ZusatzDienste.Add(s => s.Replace(ServiceDescriptor.Singleton<IMailVersand, KaputterMailVersand>()));
        using var client = factory.CreateClient();

        using var antwort = await PostAsync(client, "/api/konten/login-link", new LoginLinkAnfordern(Unbekannt));

        Assert.Equal(HttpStatusCode.InternalServerError, antwort.StatusCode);
        var problem = await ProblemAsync(antwort);
        Assert.DoesNotContain(Unbekannt, problem.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1L, await WertAsync<long>(verbindung, "SELECT count(*) FROM konten.login_anfrage"));
    }

    private static async Task<HttpResponseMessage> MitTokenAsync(HttpClient client, string token, string pfad, object koerper)
    {
        using var anfrage = new HttpRequestMessage(HttpMethod.Post, pfad) { Content = System.Net.Http.Json.JsonContent.Create(koerper) };
        anfrage.Headers.Add(Zeltlotse.Api.Contracts.Gemeinsam.Kopfzeilen.Antiforgery, token);
        return await client.SendAsync(anfrage, Abbruch);
    }

    private sealed class KaputterMailVersand : IMailVersand
    {
        public Task SendenAsync(string an, string betreff, string text, CancellationToken abbruch = default) =>
            throw new InvalidOperationException($"SMTP nicht erreichbar für {an}");
    }
}
