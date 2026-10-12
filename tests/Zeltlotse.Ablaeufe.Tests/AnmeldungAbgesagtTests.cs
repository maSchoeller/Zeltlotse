using System.Net;
using Zeltlotse.Testing;

namespace Zeltlotse.Ablaeufe.Tests;

/// <summary>
/// Durchlauf-Tests (AD-28, AD-30) für die Zeile <c>AnmeldungAbgesagt</c> (Freizeiten → Personen) und den Entzug der Team-Rolle über
/// <c>FreizeitRolleGeaendert</c> (Freizeiten → Personen): Miriam sagt Emma (eingegangen) und Jonas (bestätigt) ab; die Outbox wird
/// sofort abgearbeitet und noch einmal zugestellt. Danach sind beide Freigaben zum Zeitpunkt der Zustellung beendet (die Zeilen
/// bleiben), Jonas' Zeile in der Rollenliste von Personen ist gelöscht, die Mitgliedschaft bleibt.
/// </summary>
public sealed class AnmeldungAbgesagtTests(PostgresContainer postgres)
{
    [Fact]
    public async Task Absagen_Outbox_zustellen_beendet_die_Freigaben_und_entfernt_die_Team_Rolle_aus_der_Rollenliste()
    {
        await using var ablauf = await AnmeldeAblauf.AufbauenAsync(postgres);
        await ablauf.EntscheidenAsync(ablauf.AnmeldungJonas, "bestaetigen");
        await ablauf.Factory.OutboxJetztZustellenAsync(AnmeldeAblauf.Abbruch);
        Assert.Equal(1L, await ablauf.WertAsync<long>("SELECT count(*) FROM personen.freizeit_rolle WHERE \"PersonId\" = @p", ("p", ablauf.JonasPerson)));

        await ablauf.EntscheidenAsync(ablauf.AnmeldungEmma, "absagen");
        await ablauf.EntscheidenAsync(ablauf.AnmeldungJonas, "absagen");
        var zustellung = ablauf.VolleSekunde();
        await ablauf.ZustellenMitWiederholungAsync();

        Assert.Equal(2L, await ablauf.WertAsync<long>("SELECT count(*) FROM personen.freigabe WHERE \"GueltigBis\" = @z", ("z", zustellung)));
        Assert.Equal(0L, await ablauf.WertAsync<long>("SELECT count(*) FROM personen.freizeit_rolle WHERE \"PersonId\" = @p", ("p", ablauf.JonasPerson)));
        Assert.Equal(1L, await ablauf.WertAsync<long>("SELECT count(*) FROM organisationen.mitgliedschaft WHERE \"PersonId\" = @p", ("p", ablauf.JonasPerson)));
        Assert.Equal(2L, await ablauf.WertAsync<long>("SELECT count(*) FROM personen.inbox WHERE \"Empfaenger\" = 'Zeltlotse.Personen.Freigaben.FreigabeBeenden'"));
        Assert.Equal(0L, await ablauf.WertAsync<long>("SELECT count(*) FROM freizeiten.outbox_zustellung WHERE \"ErledigtAm\" IS NULL"));

        // Eine spätere Zustellung zu einem anderen Zeitpunkt verschiebt das Ende nicht.
        ablauf.Factory.Uhr.Advance(TimeSpan.FromMinutes(5));
        await ablauf.ZustellenMitWiederholungAsync();
        Assert.Equal(2L, await ablauf.WertAsync<long>("SELECT count(*) FROM personen.freigabe WHERE \"GueltigBis\" = @z", ("z", zustellung)));

        Assert.Equal(HttpStatusCode.NotFound, (await ablauf.JonasClient.GetAsync($"/api/freizeiten/{ablauf.FreizeitId}", AnmeldeAblauf.Abbruch)).StatusCode);
    }
}
