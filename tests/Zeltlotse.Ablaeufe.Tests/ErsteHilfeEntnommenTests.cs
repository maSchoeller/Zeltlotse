using System.Net;
using System.Net.Http.Json;
using Zeltlotse.Api.Contracts.Inventar;
using Zeltlotse.Api.Contracts.Sicherheit;
using Zeltlotse.Testing;

namespace Zeltlotse.Ablaeufe.Tests;

/// <summary>
/// Durchlauf-Test (AD-28, AD-30) für die Zeile <c>ErsteHilfeEntnommen</c> (Sicherheit → Inventar, Eintrag auf der Schadens- und Verlustliste) der
/// Tabelle Ereignis-Fluss, Story 3-4: Miriam trägt im Vorfall-Protokoll von Emma eine Entnahme ein; nach dem Zustellen steht auf der Liste der
/// Freizeit ein Eintrag „Erste-Hilfe-Kasten“ mit der Kennung des Vorfall-Eintrags, ohne den Text. Derselbe Eintrag noch einmal gesendet (AD-18)
/// erzeugt kein zweites Ereignis, eine wiederholte Zustellung verwirft die Inbox; ein Eintrag der Art „Was passiert ist“ erzeugt keines.
/// </summary>
public sealed class ErsteHilfeEntnommenTests(PostgresContainer postgres)
{
    private const string Ereignisse = "SELECT count(*) FROM sicherheit.outbox WHERE \"Typ\" LIKE '%.ErsteHilfeEntnommen'";

    private static CancellationToken Abbruch => AnmeldeAblauf.Abbruch;

    [Fact]
    public async Task Entnahme_im_Vorfall_Protokoll_steht_nach_der_Zustellung_genau_einmal_auf_der_Schadens_und_Verlustliste()
    {
        await using var ablauf = await AnmeldeAblauf.AufbauenAsync(postgres);
        var vorfall = Guid.CreateVersion7();
        var eintrag = new VorfallEintragNeu(vorfall, ablauf.Emma, VorfallArt.Entnahme, "2 Pflaster, 1 sterile Kompresse");

        await VorfallAsync(ablauf, eintrag);
        await VorfallAsync(ablauf, eintrag);
        Assert.Equal(1L, await ablauf.WertAsync<long>(Ereignisse));
        Assert.Empty(await ListeAsync(ablauf));

        await ablauf.ZustellenMitWiederholungAsync();
        await ablauf.AusfuehrenAsync("UPDATE sicherheit.outbox_zustellung SET \"ErledigtAm\" = NULL, \"NaechsterVersuch\" = '2000-01-01'");
        await ablauf.Factory.OutboxJetztZustellenAsync(Abbruch);

        var schaden = Assert.Single(await ListeAsync(ablauf));
        Assert.Equal((SchadenArt.Entnahme, "Erste-Hilfe-Kasten", (Guid?)vorfall), (schaden.Art, schaden.Bezeichnung, schaden.VorfallEintragId));
        var erfasst = await ablauf.WertAsync<DateTime>("SELECT \"ErfasstAm\" FROM sicherheit.vorfall_eintrag WHERE \"Id\" = @v", ("v", vorfall));
        Assert.Equal(new DateTimeOffset(erfasst, TimeSpan.Zero), schaden.ErfasstAm);
        Assert.Equal(1L, await ablauf.WertAsync<long>("SELECT count(*) FROM inventar.inbox WHERE \"Empfaenger\" LIKE '%ErsteHilfeEntnahmeEintragen%'"));
        Assert.Equal(0L, await ablauf.WertAsync<long>("SELECT count(*) FROM inventar.schaden_verlust WHERE \"Bezeichnung\" LIKE '%Pflaster%'"));
        Assert.Equal(1L, await ablauf.WertAsync<long>("SELECT \"Folgenummer\" FROM sicherheit.vorfall_eintrag WHERE \"Id\" = @v", ("v", vorfall)));
    }

    [Fact]
    public async Task Was_passiert_ist_und_Medikament_erzeugen_kein_Ereignis_und_keinen_Eintrag()
    {
        await using var ablauf = await AnmeldeAblauf.AufbauenAsync(postgres);

        await VorfallAsync(ablauf, new VorfallEintragNeu(Guid.CreateVersion7(), ablauf.Emma, VorfallArt.Geschehen, "Knie aufgeschlagen"));
        await VorfallAsync(ablauf, new VorfallEintragNeu(Guid.CreateVersion7(), ablauf.Emma, VorfallArt.Medikament, "Kühlpack"));
        await ablauf.Factory.OutboxJetztZustellenAsync(Abbruch);

        Assert.Equal(0L, await ablauf.WertAsync<long>(Ereignisse));
        Assert.Empty(await ListeAsync(ablauf));
    }

    private static async Task VorfallAsync(AnmeldeAblauf ablauf, VorfallEintragNeu eintrag)
    {
        using var antwort = await AnmeldeAblauf.SendenAsync(ablauf.MiriamClient, $"/api/freizeiten/{ablauf.FreizeitId}/sicherheit/vorfaelle", eintrag);
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
    }

    private static async Task<List<SchadenEintrag>> ListeAsync(AnmeldeAblauf ablauf) =>
        (await ablauf.MiriamClient.GetFromJsonAsync<List<SchadenEintrag>>($"/api/freizeiten/{ablauf.FreizeitId}/inventar/schaeden", Abbruch))!;
}
