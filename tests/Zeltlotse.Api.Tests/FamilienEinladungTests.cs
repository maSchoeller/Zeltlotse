using System.Net;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.FamilienHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Familien-Einladung über HTTP (FR-3, AD-11, AD-30 Token-Pflichtfälle), Matrix-Zeilen „Einladen“, „Einladung annehmen“,
/// „Einladung Sonderfälle“ und „Token-Fälle“: gültig; abgelaufen, benutzt, verworfen, anderer Zweck (Org-Einladung, Login,
/// Übergabe), unbekannt, Formfehler, andere Mail → <c>404</c> mit Protokolleintrag und ohne Änderung.
/// </summary>
public sealed class FamilienEinladungTests(PostgresContainer postgres)
{
    private sealed record Lage(ZeltlotseApiFactory Factory, string Verbindung, HttpClient Katrin, Guid Emma) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Katrin.Dispose();
            await Factory.DisposeAsync();
        }
    }

    /// <summary>Katrin mit Emma im Familienkonto und einer Einladung an Thomas.</summary>
    private async Task<Lage> LageAsync(bool einladen = true)
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        var factory = OrgHelfer.Factory(verbindung);
        var katrin = await AngemeldeterClientAsync(factory, Katrin);
        var emma = await KindAsync(katrin, "Emma");
        if (einladen)
        {
            using var antwort = await EinladenAsync(katrin, Thomas);
            Assert.Equal(HttpStatusCode.Accepted, antwort.StatusCode);
        }

        return new Lage(factory, verbindung, katrin, emma);
    }

    private static Task<long> ElternAsync(Lage lage) => WertAsync<long>(lage.Verbindung, "SELECT count(*) FROM personen.familien_elternteil");

    [Fact]
    public async Task Einladen_antwortet_202_und_schickt_die_Mail_ohne_Anrede()
    {
        await using var lage = await LageAsync(einladen: false);

        using var antwort = await EinladenAsync(lage.Katrin, " Thomas.Muster@example.org ");

        Assert.Equal(HttpStatusCode.Accepted, antwort.StatusCode);
        var json = await JsonAsync(antwort);
        Assert.Equal(["id"], Namen(json));
        var mail = lage.Factory.Mails.LetzteAn(Thomas);
        Assert.Equal(EinladungsBetreff, mail.Betreff);
        Assert.Contains("https://localhost/app/familie/einladung/", mail.Link, StringComparison.Ordinal);
        Assert.Contains("gilt 7 Tage", mail.Text, StringComparison.Ordinal);
        Assert.Contains("Zum Annehmen mit dieser Mail-Adresse anmelden.", mail.Text, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\b(Sie|du|Du|dein|Ihr)\b", mail.Text);
        var offen = Assert.Single((await FamilieAsync(lage.Katrin)).Einladungen);
        Assert.Equal((json.GetProperty("id").GetGuid(), Thomas), (offen.Id, offen.Mail));
        Assert.Equal(lage.Factory.Uhr.GetUtcNow() + TimeSpan.FromDays(7), offen.GueltigBis, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Einladen_mit_ungueltiger_Mail_ergibt_400_und_bei_zwei_Eltern_409()
    {
        await using var lage = await LageAsync(einladen: false);

        using var ungueltig = await EinladenAsync(lage.Katrin, "kein-mail");
        Assert.Equal(HttpStatusCode.BadRequest, ungueltig.StatusCode);
        Assert.True((await ProblemAsync(ungueltig)).GetProperty("errors").TryGetProperty("mail", out _));

        using var thomas = await ZweiterElternteilAsync(lage.Factory, lage.Katrin, Thomas);
        using var voll = await EinladenAsync(lage.Katrin, "dritte@example.org");
        using var vollVomZweiten = await EinladenAsync(thomas, "dritte@example.org");

        Assert.Equal(HttpStatusCode.Conflict, voll.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(voll)));
        Assert.Equal(HttpStatusCode.Conflict, vollVomZweiten.StatusCode);
        Assert.Empty(lage.Factory.Mails.An("dritte@example.org"));
    }

    [Fact]
    public async Task Einladung_annehmen_macht_zum_zweiten_Elternteil_und_protokolliert_die_Rollenvergabe()
    {
        await using var lage = await LageAsync();
        var token = EinladungsToken(lage.Factory, Thomas);
        using var thomas = await AngemeldeterClientAsync(lage.Factory, Thomas);

        using var antwort = await EinladungAnnehmenAsync(thomas, token);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        Assert.Empty(Namen(await JsonAsync(antwort)));
        Assert.Equal([lage.Emma], (await FamilieAsync(thomas)).Kinder.Select(k => k.PersonId));
        Assert.Empty((await FamilieAsync(lage.Katrin)).Einladungen);
        var personId = await PersonIdAsync(lage.Verbindung, Thomas);
        Assert.Equal(1L, await WertAsync<long>(
            lage.Verbindung,
            "SELECT count(*) FROM betrieb.sicherheitsprotokoll WHERE \"Art\" = @art AND \"PersonId\" = @person AND \"BezugId\" IS NOT NULL",
            ("art", (short)SicherheitsEreignisArt.Rollenvergabe), ("person", personId)));
    }

    [Fact]
    public async Task Schon_Elternteil_derselben_Familie_ergibt_200_ohne_Aenderung()
    {
        await using var lage = await LageAsync(einladen: false);
        using (var antwort = await EinladenAsync(lage.Katrin, Katrin))
        {
            Assert.Equal(HttpStatusCode.Accepted, antwort.StatusCode);
        }

        using var annehmen = await EinladungAnnehmenAsync(lage.Katrin, EinladungsToken(lage.Factory, Katrin));

        Assert.Equal(HttpStatusCode.OK, annehmen.StatusCode);
        Assert.Equal(1L, await ElternAsync(lage));
        Assert.Equal(2, await WertAsync<short>(lage.Verbindung, "SELECT \"Status\" FROM personen.familien_einladung"));
    }

    [Fact]
    public async Task Elternteil_einer_anderen_Familie_ergibt_409_ohne_Aenderung()
    {
        await using var lage = await LageAsync(einladen: false);
        using var daniel = await AngemeldeterClientAsync(lage.Factory, Daniel);
        await KindAsync(daniel, "Mia", new DateOnly(2016, 5, 21), "Probst");
        using (var antwort = await EinladenAsync(lage.Katrin, Daniel))
        {
            Assert.Equal(HttpStatusCode.Accepted, antwort.StatusCode);
        }

        using var annehmen = await EinladungAnnehmenAsync(daniel, EinladungsToken(lage.Factory, Daniel));

        Assert.Equal(HttpStatusCode.Conflict, annehmen.StatusCode);
        Assert.Equal(Problemcodes.Konflikt, Code(await ProblemAsync(annehmen)));
        Assert.Equal(2L, await ElternAsync(lage));
        Assert.Equal(1, await WertAsync<short>(lage.Verbindung, "SELECT \"Status\" FROM personen.familien_einladung"));
        Assert.Equal(["Mia"], (await FamilieAsync(daniel)).Kinder.Select(k => k.Vorname));
    }

    [Fact]
    public async Task Inzwischen_volle_Familie_ergibt_409_ohne_Aenderung()
    {
        await using var lage = await LageAsync();
        await AusfuehrenAsync(lage.Verbindung, "INSERT INTO personen.familien_elternteil (\"FamilieId\", \"PersonId\") SELECT \"Id\", @p FROM personen.familie", ("p", Guid.CreateVersion7()));
        using var thomas = await AngemeldeterClientAsync(lage.Factory, Thomas);

        using var annehmen = await EinladungAnnehmenAsync(thomas, EinladungsToken(lage.Factory, Thomas));

        Assert.Equal(HttpStatusCode.Conflict, annehmen.StatusCode);
        Assert.Equal(2L, await ElternAsync(lage));
        Assert.Empty((await FamilieAsync(thomas)).Kinder);
    }

    public static TheoryData<string> TokenFaelle =>
        ["abgelaufen", "zweites Annehmen", "durch neue Einladung verworfen", "Org-Einladungs-Token", "Login-Token", "Übergabe-Token", "unbekannt", "Formfehler", "kein Token", "andere Mail"];

    [Theory]
    [MemberData(nameof(TokenFaelle))]
    public async Task Ungueltiges_Token_ergibt_404_ohne_Aenderung_und_einen_Eintrag_im_Sicherheitsprotokoll(string fall)
    {
        await using var lage = await LageAsync();
        var token = EinladungsToken(lage.Factory, Thomas);
        var original = SharedKernel.Zugangslinks.Zugangslink.TokenHash("familien-einladung", token);
        using var client = await AngemeldeterClientAsync(lage.Factory, fall == "andere Mail" ? Daniel : Thomas);
        var (eltern, status) = (1L, (short)1);
        switch (fall)
        {
            case "abgelaufen":
                lage.Factory.Uhr.Advance(TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1));
                break;
            case "zweites Annehmen":
                using (var erstes = await EinladungAnnehmenAsync(client, token))
                {
                    Assert.Equal(HttpStatusCode.OK, erstes.StatusCode);
                }

                (eltern, status) = (2, 2);
                break;
            case "durch neue Einladung verworfen":
                using (var neu = await EinladenAsync(lage.Katrin, "andere@example.org"))
                {
                    Assert.Equal(HttpStatusCode.Accepted, neu.StatusCode);
                }

                status = 3;
                break;
            case "Org-Einladungs-Token":
                using (var gerd = await AngemeldeterClientAsync(lage.Factory, OrgHelfer.Gerd))
                {
                    await OrgHelfer.AnlegenAsync(gerd, "Jugendwerk", Thomas);
                }

                token = OrgHelfer.Token(lage.Factory, Thomas);
                break;
            case "Login-Token":
                await LinkAnfordernAsync(lage.Factory.CreateClient(), Thomas);
                token = lage.Factory.Mails.An(Thomas).Last(m => m.Betreff == "Anmelden bei Zeltlotse").Token;
                break;
            case "Übergabe-Token":
                var paul = await VolljaehrigesKindAsync(lage.Katrin, "Paul");
                using (var uebergabe = await UebergebenAsync(lage.Katrin, paul, Thomas))
                {
                    Assert.Equal(HttpStatusCode.Accepted, uebergabe.StatusCode);
                }

                token = UebergabeToken(lage.Factory, Thomas);
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
        using var antwort = await EinladungAnnehmenAsync(client, token);

        Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
        Assert.Equal(vorher + 1, await ProtokollAsync(lage.Verbindung, SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen));
        Assert.Equal(eltern, await ElternAsync(lage));
        Assert.Equal(status, await WertAsync<short>(lage.Verbindung, "SELECT \"Status\" FROM personen.familien_einladung WHERE \"TokenHash\" = @hash", ("hash", original)));
        if (fall == "abgelaufen")
        {
            // Abgelaufene Einladungen zeigt GET /api/ich/familie nicht mehr.
            Assert.Empty((await FamilieAsync(lage.Katrin)).Einladungen);
        }
    }
}
