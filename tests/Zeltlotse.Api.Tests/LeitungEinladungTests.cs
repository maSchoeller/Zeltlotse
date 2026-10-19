using System.Net;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.FreizeitHelfer;
using static Zeltlotse.Api.Tests.OrgHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Leitungs-Einladung annehmen über HTTP (AD-11, AD-30, Token-Pflichtfälle), Matrix-Zeilen „Annehmen gültig“,
/// „Token-Fälle“ und „Doppelt Leitung“: gültig; abgelaufen, benutzt, verworfen, anderer Zweck (Org-Einladung, Login),
/// unbekannt, Formfehler, andere Mail → <c>404</c> mit Protokolleintrag und ohne Änderung; Einladung für X wirkt nicht in Y.
/// </summary>
public sealed class LeitungEinladungTests(PostgresContainer postgres)
{
    private sealed record Lage(ZeltlotseApiFactory Factory, string Verbindung, HttpClient Andrea, Guid OrgId, Guid FreizeitId) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Andrea.Dispose();
            await Factory.DisposeAsync();
        }
    }

    /// <summary>Organisation mit Andrea als Org-Admin und einer Freizeit, Miriam als Leitung eingeladen.</summary>
    private async Task<Lage> LageAsync()
    {
        var verbindung = await postgres.NeueMigrierteDatenbankAsync();
        var factory = OrgHelfer.Factory(verbindung);
        using var gerd = await AngemeldeterClientAsync(factory, Gerd);
        var (orgId, andrea) = await OrganisationMitOrgAdminAsync(factory, gerd, "Jugendwerk", Andrea);
        var freizeit = await AnlegenAsync(andrea, orgId);
        await FreizeitHelfer.EinladenAsync(andrea, orgId, freizeit, Miriam);
        return new Lage(factory, verbindung, andrea, orgId, freizeit);
    }

    [Fact]
    public async Task Gueltige_Einladung_setzt_die_Leitung_protokolliert_die_Rollenvergabe_und_legt_das_Ereignis_in_die_Outbox()
    {
        await using var lage = await LageAsync();
        var token = LeitungsToken(lage.Factory, Miriam);
        using var miriam = await AngemeldeterClientAsync(lage.Factory, Miriam);

        using var antwort = await FreizeitHelfer.AnnehmenAsync(miriam, token);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var json = await JsonAsync(antwort);
        Assert.Equal(["freizeitId"], Namen(json));
        Assert.Equal(lage.FreizeitId, json.GetProperty("freizeitId").GetGuid());
        var personId = await PersonIdAsync(lage.Verbindung, Miriam);
        Assert.Equal(1L, await WertAsync<long>(
            lage.Verbindung,
            "SELECT count(*) FROM betrieb.sicherheitsprotokoll WHERE \"Art\" = @art AND \"PersonId\" = @person AND \"OrganisationId\" = @org AND \"BezugId\" IS NOT NULL",
            ("art", (short)SicherheitsEreignisArt.Rollenvergabe), ("person", personId), ("org", lage.OrgId)));
        Assert.Equal(1L, await WertAsync<long>(lage.Verbindung, "SELECT count(*) FROM freizeiten.freizeit_rolle WHERE \"PersonId\" = @p AND \"Rolle\" = 1", ("p", personId)));
        Assert.Equal(2, await WertAsync<short>(lage.Verbindung, "SELECT \"Status\" FROM freizeiten.leitungs_einladung"));
        Assert.Equal(1L, await WertAsync<long>(lage.Verbindung, "SELECT count(*) FROM freizeiten.outbox WHERE \"Typ\" LIKE '%FreizeitRolleGeaendert'"));
        Assert.Equal(HttpStatusCode.OK, (await miriam.GetAsync($"/api/freizeiten/{lage.FreizeitId}", Abbruch)).StatusCode);
    }

    public static TheoryData<string> TokenFaelle =>
        ["abgelaufen", "zweites Annehmen", "durch neue Einladung verworfen", "Org-Einladungs-Token", "Login-Token", "unbekannt", "Formfehler", "kein Token", "andere Mail"];

    [Theory]
    [MemberData(nameof(TokenFaelle))]
    public async Task Ungueltiges_Token_ergibt_404_ohne_Aenderung_und_einen_Eintrag_im_Sicherheitsprotokoll(string fall)
    {
        await using var lage = await LageAsync();
        var token = LeitungsToken(lage.Factory, Miriam);
        var original = SharedKernel.Zugangslinks.Zugangslink.TokenHash("leitung-einladung", token);
        var mail = fall == "andere Mail" ? Tobias : Miriam;
        using var client = await AngemeldeterClientAsync(lage.Factory, mail);
        var (rollen, status) = (0L, (short)1);
        switch (fall)
        {
            case "abgelaufen":
                lage.Factory.Uhr.Advance(TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1));
                break;
            case "zweites Annehmen":
                using (var erstes = await FreizeitHelfer.AnnehmenAsync(client, token))
                {
                    Assert.Equal(HttpStatusCode.OK, erstes.StatusCode);
                }

                (rollen, status) = (1, 2);
                break;
            case "durch neue Einladung verworfen":
                await FreizeitHelfer.EinladenAsync(lage.Andrea, lage.OrgId, lage.FreizeitId, Miriam);
                status = 3;
                break;
            case "Org-Einladungs-Token":
                await OrgHelfer.EinladenAsync(lage.Andrea, lage.OrgId, Miriam, OrgRolle.OrgUeberblick);
                token = OrgHelfer.Token(lage.Factory, Miriam);
                break;
            case "Login-Token":
                await LinkAnfordernAsync(lage.Factory.CreateClient(), Miriam);
                token = lage.Factory.Mails.An(Miriam).Last(m => m.Betreff == "Anmelden bei Zeltlotse").Token;
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
        using var antwort = await FreizeitHelfer.AnnehmenAsync(client, token);

        Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
        Assert.Equal(vorher + 1, await ProtokollAsync(lage.Verbindung, SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen));
        Assert.Equal(rollen, await WertAsync<long>(lage.Verbindung, "SELECT count(*) FROM freizeiten.freizeit_rolle"));
        Assert.Equal(status, await WertAsync<short>(lage.Verbindung, "SELECT \"Status\" FROM freizeiten.leitungs_einladung WHERE \"TokenHash\" = @hash", ("hash", original)));
        Assert.Equal(rollen, await WertAsync<long>(lage.Verbindung, "SELECT count(*) FROM freizeiten.outbox"));
        Assert.Equal(fall == "zweites Annehmen" ? HttpStatusCode.OK : HttpStatusCode.NotFound, (await client.GetAsync($"/api/freizeiten/{lage.FreizeitId}", Abbruch)).StatusCode);
    }

    [Fact]
    public async Task Andere_Mail_ergibt_404_und_danach_nimmt_die_eingeladene_Person_dieselbe_Einladung_an()
    {
        await using var lage = await LageAsync();
        var token = LeitungsToken(lage.Factory, Miriam);
        using var tobias = await AngemeldeterClientAsync(lage.Factory, Tobias);

        using var falsch = await FreizeitHelfer.AnnehmenAsync(tobias, token);
        using var vomOrgAdmin = await FreizeitHelfer.AnnehmenAsync(lage.Andrea, token);

        Assert.Equal(HttpStatusCode.NotFound, falsch.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, vomOrgAdmin.StatusCode);
        using var miriam = await AngemeldeterClientAsync(lage.Factory, Miriam);
        using var richtig = await FreizeitHelfer.AnnehmenAsync(miriam, token);
        Assert.Equal(HttpStatusCode.OK, richtig.StatusCode);
    }

    [Fact]
    public async Task Einladung_fuer_X_macht_nicht_zur_Leitung_von_Y()
    {
        await using var lage = await LageAsync();
        var y = await AnlegenAsync(lage.Andrea, lage.OrgId, "Y");
        using var miriam = await AngemeldeterClientAsync(lage.Factory, Miriam);

        await AnnehmenMitAsync(lage.Factory, miriam, Miriam);

        Assert.Equal(HttpStatusCode.OK, (await miriam.GetAsync($"/api/freizeiten/{lage.FreizeitId}", Abbruch)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await miriam.GetAsync($"/api/freizeiten/{y}", Abbruch)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await miriam.GetAsync($"/api/freizeiten/{y}/kontext", Abbruch)).StatusCode);
    }

    [Fact]
    public async Task Zweite_Einladung_derselben_Person_angenommen_ergibt_eine_Rolle_und_ein_Ereignis()
    {
        await using var lage = await LageAsync();
        using var miriam = await AngemeldeterClientAsync(lage.Factory, Miriam);
        await AnnehmenMitAsync(lage.Factory, miriam, Miriam);
        await FreizeitHelfer.EinladenAsync(lage.Andrea, lage.OrgId, lage.FreizeitId, Miriam);

        await AnnehmenMitAsync(lage.Factory, miriam, Miriam);

        Assert.Equal(1L, await WertAsync<long>(lage.Verbindung, "SELECT count(*) FROM freizeiten.freizeit_rolle"));
        Assert.Equal(1L, await WertAsync<long>(lage.Verbindung, "SELECT count(*) FROM freizeiten.outbox"));
    }
}
