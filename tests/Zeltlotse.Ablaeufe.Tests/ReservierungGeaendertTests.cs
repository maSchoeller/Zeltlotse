using System.Net;
using System.Net.Http.Json;
using Zeltlotse.Api.Contracts.Inventar;
using Zeltlotse.Api.Contracts.Programm;
using Zeltlotse.Testing;

namespace Zeltlotse.Ablaeufe.Tests;

/// <summary>
/// Durchlauf-Test (AD-28, AD-30) für die Zeile <c>ReservierungGeaendert</c> (Inventar → Programm, Materialstatus aktualisieren) der Tabelle
/// Ereignis-Fluss, Story 3-3: Miriam legt am Geländespiel das Material „Seile“ an, Andrea den Artikel „Seile“ im Inventar. Miriam reserviert ihn
/// für das Material; nach dem Zustellen ist das Material „vorhanden“ und trägt die Kennung der Reservierung. Nach dem Aufheben und Zustellen ist
/// es wieder „gebraucht“ ohne Kennung. Ein wiederholtes Zustellen verwirft die Inbox: genau ein Eintrag je Ereignis.
/// </summary>
public sealed class ReservierungGeaendertTests(PostgresContainer postgres)
{
    private static CancellationToken Abbruch => AnmeldeAblauf.Abbruch;

    [Fact]
    public async Task Reservieren_und_Aufheben_fuer_ein_Material_setzen_seinen_Status_ueber_die_Outbox()
    {
        await using var ablauf = await AnmeldeAblauf.AufbauenAsync(postgres);
        var programm = $"/api/freizeiten/{ablauf.FreizeitId}/programm";
        var punkt = await IdAsync(await AnmeldeAblauf.SendenAsync(ablauf.MiriamClient, $"{programm}/punkte", new { titel = "Geländespiel", tag = "2027-08-03" }));
        var material = await IdAsync(await AnmeldeAblauf.SendenAsync(ablauf.MiriamClient, $"{programm}/punkte/{punkt}/material", new MaterialNeu("Seile", "4 × 10 m")));
        var artikel = await IdAsync(await AnmeldeAblauf.SendenAsync(ablauf.AndreaClient, $"/api/organisationen/{ablauf.OrgId}/inventar", new InventarArtikelNeu("Seile", "4 Stück")));

        var reservierung = await IdAsync(await AnmeldeAblauf.SendenAsync(
            ablauf.MiriamClient, $"/api/freizeiten/{ablauf.FreizeitId}/inventar/{artikel}/reservierungen", new ReservierungNeu(null, null, material)));
        Assert.Equal((MaterialStatus.Gebraucht, (Guid?)null), await StandAsync(ablauf, material));

        await ablauf.ZustellenMitWiederholungAsync();
        Assert.Equal((MaterialStatus.Vorhanden, (Guid?)reservierung), await StandAsync(ablauf, material));
        Assert.Equal(1L, await InboxAsync(ablauf));

        using (var aufheben = await AnmeldeAblauf.SendenAsync(ablauf.MiriamClient, $"/api/freizeiten/{ablauf.FreizeitId}/inventar/reservierungen/{reservierung}", null, HttpMethod.Delete))
        {
            Assert.Equal(HttpStatusCode.NoContent, aufheben.StatusCode);
        }

        await ablauf.ZustellenMitWiederholungAsync();
        await ablauf.AusfuehrenAsync("UPDATE inventar.outbox_zustellung SET \"ErledigtAm\" = NULL, \"NaechsterVersuch\" = '2000-01-01'");
        await ablauf.Factory.OutboxJetztZustellenAsync(Abbruch);
        Assert.Equal((MaterialStatus.Gebraucht, (Guid?)null), await StandAsync(ablauf, material));
        Assert.Equal(2L, await InboxAsync(ablauf));
        Assert.Equal(2L, await ablauf.WertAsync<long>("SELECT count(*) FROM inventar.outbox WHERE \"Typ\" LIKE '%.ReservierungGeaendert'"));
    }

    private static Task<long> InboxAsync(AnmeldeAblauf ablauf) =>
        ablauf.WertAsync<long>("SELECT count(*) FROM programm.inbox WHERE \"Empfaenger\" LIKE '%ProgrammReservierungUebernehmen%'");

    private static async Task<(MaterialStatus, Guid?)> StandAsync(AnmeldeAblauf ablauf, Guid material)
    {
        var liste = await ablauf.MiriamClient.GetFromJsonAsync<Materialliste>($"/api/freizeiten/{ablauf.FreizeitId}/programm/material", Abbruch);
        var eintrag = liste!.Material.Single(m => m.Id == material);
        return (eintrag.Status, eintrag.ReservierungId);
    }

    private static async Task<Guid> IdAsync(HttpResponseMessage antwort)
    {
        using (antwort)
        {
            Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
            return (await antwort.Content.ReadFromJsonAsync<IdAntwort>(Abbruch))!.Id;
        }
    }

    private sealed record IdAntwort(Guid Id);
}
