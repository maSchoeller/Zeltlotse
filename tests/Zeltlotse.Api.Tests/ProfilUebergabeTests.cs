using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Konten;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.FamilienHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Profil-Übergabe mit 18 über HTTP (FR-4, AD-7, AD-11, AD-25 erneute Anmeldung, AD-30), Matrix-Zeilen „Übergabe auslösen“,
/// „Übergabe annehmen“ und „Token-Fälle“: Auslösen nur bei volljährigem Kind und frischer Anmeldung, Annehmen nur mit leerem
/// eigenen Profil, jedes ungültige Token → <c>404</c> mit Protokolleintrag und ohne Änderung.
/// </summary>
public sealed class ProfilUebergabeTests(PostgresContainer postgres)
{
    private sealed record Lage(ZeltlotseApiFactory Factory, string Verbindung, HttpClient Katrin, Guid Paul) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Katrin.Dispose();
            await Factory.DisposeAsync();
        }
    }

    /// <summary>Katrin mit dem volljährigen Paul im Familienkonto, frisch angemeldet.</summary>
    private async Task<Lage> LageAsync()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        var factory = OrgHelfer.Factory(verbindung);
        var katrin = await AngemeldeterClientAsync(factory, Katrin);
        var paul = await VolljaehrigesKindAsync(katrin, "Paul");
        return new Lage(factory, verbindung, katrin, paul);
    }

    /// <summary>Katrin übergibt Paul an <see cref="FamilienHelfer.PaulMail"/>; liefert das Token aus der Mail.</summary>
    private static async Task<string> UebergebenAsync(Lage lage)
    {
        using var antwort = await FamilienHelfer.UebergebenAsync(lage.Katrin, lage.Paul, PaulMail);
        Assert.Equal(HttpStatusCode.Accepted, antwort.StatusCode);
        return UebergabeToken(lage.Factory, PaulMail);
    }

    [Fact]
    public async Task Ausloesen_antwortet_202_schickt_die_Mail_und_verwirft_eine_aeltere_Uebergabe()
    {
        await using var lage = await LageAsync();

        using var erste = await FamilienHelfer.UebergebenAsync(lage.Katrin, lage.Paul, "alt@example.org");
        using var antwort = await FamilienHelfer.UebergebenAsync(lage.Katrin, lage.Paul, " Paul.Muster@example.org ");

        Assert.Equal(HttpStatusCode.Accepted, erste.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, antwort.StatusCode);
        Assert.Equal(["id"], Namen(await JsonAsync(antwort)));
        var mail = lage.Factory.Mails.LetzteAn(PaulMail);
        Assert.Equal(UebergabeBetreff, mail.Betreff);
        Assert.Contains("https://localhost/app/uebergabe/", mail.Link, StringComparison.Ordinal);
        Assert.Contains("Paul", mail.Text, StringComparison.Ordinal);
        Assert.Contains("gilt 14 Tage", mail.Text, StringComparison.Ordinal);
        Assert.Contains("Zum Annehmen mit dieser Mail-Adresse anmelden.", mail.Text, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\b(Sie|du|Du|dein|Ihr)\b", mail.Text);
        var kind = Assert.Single((await FamilieAsync(lage.Katrin)).Kinder);
        Assert.Equal(PaulMail, kind.Uebergabe!.Mail);
        Assert.Equal(lage.Factory.Uhr.GetUtcNow() + TimeSpan.FromDays(14), kind.Uebergabe.GueltigBis, TimeSpan.FromMilliseconds(1));
        Assert.Equal("1,3", await WertAsync<string>(lage.Verbindung, "SELECT string_agg(\"Status\"::text, ',' ORDER BY \"Status\") FROM personen.profil_uebergabe"));
    }

    [Fact]
    public async Task Kind_unter_18_ergibt_409_fremdes_Kind_404_und_ungueltige_Mail_400()
    {
        await using var lage = await LageAsync();
        var emma = await KindAsync(lage.Katrin, "Emma");

        using var jung = await FamilienHelfer.UebergebenAsync(lage.Katrin, emma, PaulMail);
        using var unbekannt = await FamilienHelfer.UebergebenAsync(lage.Katrin, Guid.CreateVersion7(), PaulMail);
        using var mail = await FamilienHelfer.UebergebenAsync(lage.Katrin, lage.Paul, "kein-mail");

        Assert.Equal(HttpStatusCode.Conflict, jung.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(jung)));
        Assert.Equal(HttpStatusCode.NotFound, unbekannt.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, mail.StatusCode);
        Assert.Equal(0L, await WertAsync<long>(lage.Verbindung, "SELECT count(*) FROM personen.profil_uebergabe"));
        Assert.Empty(lage.Factory.Mails.An(PaulMail));
    }

    [Fact]
    public async Task Anmeldung_aelter_als_10_Minuten_ergibt_401_ProblemDetails_und_eine_neue_Anmeldung_hilft()
    {
        await using var lage = await LageAsync();
        lage.Factory.Uhr.Advance(TimeSpan.FromMinutes(9));
        using (var frisch = await FamilienHelfer.UebergebenAsync(lage.Katrin, lage.Paul, PaulMail))
        {
            Assert.Equal(HttpStatusCode.Accepted, frisch.StatusCode);
        }

        lage.Factory.Uhr.Advance(TimeSpan.FromMinutes(2));
        using var alt = await FamilienHelfer.UebergebenAsync(lage.Katrin, lage.Paul, PaulMail);

        Assert.Equal(HttpStatusCode.Unauthorized, alt.StatusCode);
        await ProblemAsync(alt);
        Assert.Equal(1L, await WertAsync<long>(lage.Verbindung, "SELECT count(*) FROM personen.profil_uebergabe"));
        // Die Sitzung selbst gilt weiter; nur die Übergabe verlangt die erneute Anmeldung.
        Assert.Equal(HttpStatusCode.OK, (await lage.Katrin.GetAsync("/api/ich/familie", Abbruch)).StatusCode);

        await AnmeldenAsync(lage.Factory, lage.Katrin, Katrin);
        using var erneut = await FamilienHelfer.UebergebenAsync(lage.Katrin, lage.Paul, PaulMail);
        Assert.Equal(HttpStatusCode.Accepted, erneut.StatusCode);
    }

    [Fact]
    public async Task Sitzung_ohne_Anmelde_Zeitpunkt_ergibt_401_bei_der_Uebergabe_sonst_aber_200()
    {
        await using var lage = await LageAsync();
        var kontoId = await WertAsync<Guid>(lage.Verbindung, "SELECT \"Id\" FROM konten.konto WHERE \"Mail\" = @m", ("m", Katrin));
        var personId = await PersonIdAsync(lage.Verbindung, Katrin);
        var stempel = await WertAsync<Guid>(lage.Verbindung, "SELECT \"SitzungsStempel\" FROM konten.konto WHERE \"Mail\" = @m", ("m", Katrin));
        // Ein Sitzungs-Cookie wie vor dieser Version: nur Konto, Person und Stempel, ohne den Zeitpunkt der Anmeldung.
        var optionen = lage.Factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(SharedKernel.Anmeldung.Sitzung.Schema);
        var ticket = new AuthenticationTicket(SharedKernel.Anmeldung.Sitzung.Prinzipal(kontoId, personId, stempel), new AuthenticationProperties { IsPersistent = true }, SharedKernel.Anmeldung.Sitzung.Schema);
        var sitzung = $"{SharedKernel.Anmeldung.Sitzung.CookieName}={optionen.TicketDataFormat.Protect(ticket)}";
        using var client = lage.Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, BaseAddress = new Uri("https://localhost") });

        using var tokenAnfrage = new HttpRequestMessage(HttpMethod.Get, "/api/betrieb/antiforgery");
        tokenAnfrage.Headers.Add("Cookie", sitzung);
        using var tokenAntwort = await client.SendAsync(tokenAnfrage, Abbruch);
        var token = (await JsonAsync(tokenAntwort)).GetProperty("token").GetString()!;
        var antiforgeryCookie = tokenAntwort.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]).Single(c => !c.StartsWith(SharedKernel.Anmeldung.Sitzung.CookieName + "=", StringComparison.Ordinal));

        using var familie = new HttpRequestMessage(HttpMethod.Get, "/api/ich/familie");
        familie.Headers.Add("Cookie", sitzung);
        using var uebergabe = new HttpRequestMessage(HttpMethod.Post, $"/api/ich/familie/kinder/{lage.Paul}/uebergabe") { Content = JsonContent.Create(new Contracts.Personen.ProfilUebergeben(PaulMail)) };
        uebergabe.Headers.Add("Cookie", $"{sitzung}; {antiforgeryCookie}");
        uebergabe.Headers.Add(Kopfzeilen.Antiforgery, token);

        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(familie, Abbruch)).StatusCode);
        using var antwort = await client.SendAsync(uebergabe, Abbruch);
        Assert.Equal(HttpStatusCode.Unauthorized, antwort.StatusCode);
        await ProblemAsync(antwort);
        Assert.Equal(0L, await WertAsync<long>(lage.Verbindung, "SELECT count(*) FROM personen.profil_uebergabe"));
    }

    [Fact]
    public async Task Passwort_festlegen_stellt_die_Sitzung_neu_aus_und_behaelt_den_Anmelde_Zeitpunkt()
    {
        await using var lage = await LageAsync();
        lage.Factory.Uhr.Advance(TimeSpan.FromMinutes(6));
        using (var passwort = await SendenAsync(lage.Katrin, HttpMethod.Put, "/api/ich/konto/passwort", new PasswortFestlegen("ein-langes-passwort")))
        {
            Assert.Equal(HttpStatusCode.NoContent, passwort.StatusCode);
        }

        lage.Factory.Uhr.Advance(TimeSpan.FromMinutes(5));
        using var antwort = await FamilienHelfer.UebergebenAsync(lage.Katrin, lage.Paul, PaulMail);

        Assert.Equal(HttpStatusCode.Unauthorized, antwort.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await lage.Katrin.GetAsync("/api/ich/familie", Abbruch)).StatusCode);
    }

    [Fact]
    public async Task Annehmen_mit_leerem_Profil_uebernimmt_das_Profil_und_die_Eltern_sehen_es_nicht_mehr()
    {
        await using var lage = await LageAsync();
        var lina = await VolljaehrigesKindAsync(lage.Katrin, "Lina");
        var token = await UebergebenAsync(lage);
        using var paul = await AngemeldeterClientAsync(lage.Factory, PaulMail);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(paul, "/api/ich/profil")).StatusCode);
        var leer = await PersonIdAsync(lage.Verbindung, PaulMail);

        using var antwort = await UebernehmenAsync(paul, token);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var json = await JsonAsync(antwort);
        Assert.Equal(["personId"], Namen(json));
        Assert.Equal(lage.Paul, json.GetProperty("personId").GetGuid());
        Assert.Equal(0L, await WertAsync<long>(lage.Verbindung, "SELECT count(*) FROM personen.person WHERE \"Id\" = @id AND \"FamilieId\" IS NOT NULL", ("id", lage.Paul)));
        Assert.Equal(0L, await WertAsync<long>(lage.Verbindung, "SELECT count(*) FROM personen.person WHERE \"Id\" = @id", ("id", leer)));
        Assert.Equal(0L, await WertAsync<long>(lage.Verbindung, "SELECT count(*) FROM personen.personen_schluessel WHERE \"PersonId\" = @id", ("id", leer)));
        Assert.Equal(1L, await WertAsync<long>(lage.Verbindung, "SELECT count(*) FROM personen.outbox WHERE \"Typ\" LIKE '%ProfilUebergabeAngenommen'"));
        Assert.Equal(1L, await WertAsync<long>(
            lage.Verbindung,
            "SELECT count(*) FROM betrieb.sicherheitsprotokoll WHERE \"Art\" = @art AND \"PersonId\" = @person AND \"BezugId\" IS NOT NULL",
            ("art", (short)SicherheitsEreignisArt.ProfilUebergabe), ("person", lage.Paul)));

        // Die Übergabe für Paul wirkt nicht auf Lina; die Eltern verlieren nur Paul.
        Assert.Equal([lina], (await FamilieAsync(lage.Katrin)).Kinder.Select(k => k.PersonId));
        using var patch = await KindPatchAsync(lage.Katrin, lage.Paul, """{"version":1,"alterWert":"Paul","vorname":"X"}""");
        using var nochmal = await FamilienHelfer.UebergebenAsync(lage.Katrin, lage.Paul, PaulMail);
        Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, nochmal.StatusCode);
    }

    [Fact]
    public async Task Uebergabe_fuer_Kind_X_wirkt_nicht_auf_Kind_Y()
    {
        await using var lage = await LageAsync();
        var lina = await VolljaehrigesKindAsync(lage.Katrin, "Lina");
        var token = await UebergebenAsync(lage);
        using (var linaUebergabe = await FamilienHelfer.UebergebenAsync(lage.Katrin, lina, "lina.muster@example.org"))
        {
            Assert.Equal(HttpStatusCode.Accepted, linaUebergabe.StatusCode);
        }

        using var paul = await AngemeldeterClientAsync(lage.Factory, PaulMail);
        using var antwort = await UebernehmenAsync(paul, token);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        Assert.Equal(lage.Paul, (await JsonAsync(antwort)).GetProperty("personId").GetGuid());
        var verbleibend = Assert.Single((await FamilieAsync(lage.Katrin)).Kinder);
        Assert.Equal(lina, verbleibend.PersonId);
        Assert.Equal("lina.muster@example.org", verbleibend.Uebergabe?.Mail);
        Assert.Equal(1, await WertAsync<short>(lage.Verbindung, "SELECT \"Status\" FROM personen.profil_uebergabe WHERE \"PersonId\" = @p", ("p", lina)));
    }

    public static TheoryData<string> EigeneProfile => ["Name", "Elternteil"];

    [Theory]
    [MemberData(nameof(EigeneProfile))]
    public async Task Annehmen_mit_eigenem_Profil_ergibt_409_ohne_Aenderung(string fall)
    {
        await using var lage = await LageAsync();
        var token = await UebergebenAsync(lage);
        using var paul = await AngemeldeterClientAsync(lage.Factory, PaulMail);
        if (fall == "Name")
        {
            await FreizeitHelfer.NameSetzenAsync(paul, "Paul", "Muster");
        }
        else
        {
            await KindAsync(paul, "Ben");
        }

        using var antwort = await UebernehmenAsync(paul, token);

        Assert.Equal(HttpStatusCode.Conflict, antwort.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(antwort)));
        Assert.Contains(lage.Paul, (await FamilieAsync(lage.Katrin)).Kinder.Select(k => k.PersonId));
        Assert.Equal(0L, await WertAsync<long>(lage.Verbindung, "SELECT count(*) FROM personen.outbox"));
        Assert.Equal(1, await WertAsync<short>(lage.Verbindung, "SELECT \"Status\" FROM personen.profil_uebergabe"));
    }

    public static TheoryData<string> TokenFaelle =>
        ["abgelaufen", "zweites Annehmen", "durch neue Übergabe verworfen", "Familien-Token", "Org-Einladungs-Token", "Login-Token", "unbekannt", "Formfehler", "kein Token", "andere Mail"];

    [Theory]
    [MemberData(nameof(TokenFaelle))]
    public async Task Ungueltiges_Token_ergibt_404_ohne_Aenderung_und_einen_Eintrag_im_Sicherheitsprotokoll(string fall)
    {
        await using var lage = await LageAsync();
        var token = await UebergebenAsync(lage);
        var original = SharedKernel.Zugangslinks.Zugangslink.TokenHash("profil-uebergabe", token);
        using var client = await AngemeldeterClientAsync(lage.Factory, fall == "andere Mail" ? Thomas : PaulMail);
        var (familie, status) = (true, (short)1);
        switch (fall)
        {
            case "abgelaufen":
                lage.Factory.Uhr.Advance(TimeSpan.FromDays(14) + TimeSpan.FromMinutes(1));
                break;
            case "zweites Annehmen":
                using (var erstes = await UebernehmenAsync(client, token))
                {
                    Assert.Equal(HttpStatusCode.OK, erstes.StatusCode);
                }

                (familie, status) = (false, 2);
                break;
            case "durch neue Übergabe verworfen":
                using (var neu = await FamilienHelfer.UebergebenAsync(lage.Katrin, lage.Paul, "neu@example.org"))
                {
                    Assert.Equal(HttpStatusCode.Accepted, neu.StatusCode);
                }

                status = 3;
                break;
            case "Familien-Token":
                using (var einladen = await EinladenAsync(lage.Katrin, PaulMail))
                {
                    Assert.Equal(HttpStatusCode.Accepted, einladen.StatusCode);
                }

                token = EinladungsToken(lage.Factory, PaulMail);
                break;
            case "Org-Einladungs-Token":
                using (var gerd = await AngemeldeterClientAsync(lage.Factory, OrgHelfer.Gerd))
                {
                    await OrgHelfer.AnlegenAsync(gerd, "Jugendwerk", PaulMail);
                }

                token = OrgHelfer.Token(lage.Factory, PaulMail);
                break;
            case "Login-Token":
                await LinkAnfordernAsync(lage.Factory.CreateClient(), PaulMail);
                token = lage.Factory.Mails.An(PaulMail).Last(m => m.Betreff == "Anmelden bei Zeltlotse").Token;
                break;
            case "unbekannt":
                token = SharedKernel.Zugangslinks.Zugangslink.NeuesToken();
                break;
            case "Formfehler":
                token = token[..20] + "!";
                break;
            case "kein Token":
                token = null!;
                break;
        }

        var vorher = await ProtokollAsync(lage.Verbindung, SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen);
        using var antwort = await UebernehmenAsync(client, token);

        Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
        Assert.Equal(vorher + 1, await ProtokollAsync(lage.Verbindung, SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen));
        Assert.Equal(familie, (await FamilieAsync(lage.Katrin)).Kinder.Any(k => k.PersonId == lage.Paul));
        Assert.Equal(status, await WertAsync<short>(lage.Verbindung, "SELECT \"Status\" FROM personen.profil_uebergabe WHERE \"TokenHash\" = @hash", ("hash", original)));
        Assert.Equal(fall == "zweites Annehmen" ? 1L : 0L, await WertAsync<long>(lage.Verbindung, "SELECT count(*) FROM personen.outbox"));
        if (fall == "abgelaufen")
        {
            // Abgelaufene Übergaben zeigt GET /api/ich/familie nicht mehr.
            Assert.Null((await FamilieAsync(lage.Katrin)).Kinder.Single(k => k.PersonId == lage.Paul).Uebergabe);
        }
    }
}
