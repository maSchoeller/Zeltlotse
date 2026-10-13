using System.Net;
using Zeltlotse.Testing;

namespace Zeltlotse.Ablaeufe.Tests;

/// <summary>
/// Durchlauf-Tests (AD-28, AD-30) für die Zeile <c>AnmeldungBestaetigt</c> (Freizeiten → Personen, Organisationen) und die
/// Team-Vergabe über <c>FreizeitRolleGeaendert</c> (Freizeiten → Personen): Miriam bestätigt Emma (Teilnehmende) und Jonas
/// (Mitarbeitende); die Outbox wird sofort abgearbeitet und danach noch einmal zugestellt. Danach haben beide eine Freigabe der Stufe
/// Team, nur Jonas ist Mitglied der Organisation und steht mit Rolle Team in der Rollenliste von Personen; die Inbox verwirft die
/// Wiederholung.
/// </summary>
public sealed class AnmeldungBestaetigtTests(PostgresContainer postgres)
{
    [Fact]
    public async Task Bestaetigen_Outbox_zustellen_ergibt_Freigabe_Team_Mitgliedschaft_nur_fuer_Mitarbeitende_und_die_Team_Rolle()
    {
        await using var ablauf = await AnmeldeAblauf.AufbauenAsync(postgres);
        // Story 2-5: Bis zur Bestätigung wirken Änderungen der Auswahl; die Bestätigung trägt die Fragen von jetzt.
        await ablauf.FragenWaehlenAsync(Zeltlotse.Api.Contracts.Freizeiten.FragenFeld.Teilnehmende, "schwimmen,allergien");

        await ablauf.EntscheidenAsync(ablauf.AnmeldungEmma, "bestaetigen");
        await ablauf.EntscheidenAsync(ablauf.AnmeldungJonas, "bestaetigen");
        await ablauf.ZustellenMitWiederholungAsync();

        Assert.Equal(2L, await ablauf.WertAsync<long>("SELECT count(*) FROM personen.freigabe"));
        Assert.Equal(2L, await ablauf.WertAsync<long>(
            "SELECT count(*) FROM personen.freigabe WHERE \"Stufe\" = 2 AND \"AnmeldungId\" IN (@e, @j) AND \"OrganisationId\" = @o",
            ("e", ablauf.AnmeldungEmma), ("j", ablauf.AnmeldungJonas), ("o", ablauf.OrgId)));
        Assert.Equal(1L, await ablauf.WertAsync<long>("SELECT count(*) FROM organisationen.mitgliedschaft"));
        Assert.Equal(1L, await ablauf.WertAsync<long>(
            "SELECT count(*) FROM organisationen.mitgliedschaft WHERE \"OrganisationId\" = @o AND \"PersonId\" = @p", ("o", ablauf.OrgId), ("p", ablauf.JonasPerson)));
        Assert.Equal(1L, await ablauf.WertAsync<long>(
            "SELECT count(*) FROM personen.freizeit_rolle WHERE \"FreizeitId\" = @f AND \"PersonId\" = @p AND \"Rolle\" = 2", ("f", ablauf.FreizeitId), ("p", ablauf.JonasPerson)));
        Assert.Equal(0L, await ablauf.WertAsync<long>("SELECT count(*) FROM personen.freizeit_rolle WHERE \"PersonId\" = @p", ("p", ablauf.Emma)));
        Assert.Equal(["allergien", "schwimmen"], await ablauf.FreigabeFragenAsync(ablauf.AnmeldungEmma));
        Assert.Equal(["notfallkontakte"], await ablauf.FreigabeFragenAsync(ablauf.AnmeldungJonas));

        // Danach fest: Eine spätere Änderung der Auswahl erweitert keine bestehende Freigabe.
        await ablauf.FragenWaehlenAsync(Zeltlotse.Api.Contracts.Freizeiten.FragenFeld.Teilnehmende, "allergien,krankheiten,schwimmen");
        await ablauf.ZustellenMitWiederholungAsync();
        Assert.Equal(["allergien", "schwimmen"], await ablauf.FreigabeFragenAsync(ablauf.AnmeldungEmma));

        // Je Empfänger genau eine Inbox-Zeile je Ereignis, die Wiederholung hat nichts verdoppelt.
        Assert.Equal(2L, await ablauf.WertAsync<long>("SELECT count(*) FROM personen.inbox WHERE \"Empfaenger\" = 'Zeltlotse.Personen.Freigaben.FreigabeTeamSetzen'"));
        Assert.Equal(2L, await ablauf.WertAsync<long>("SELECT count(*) FROM organisationen.inbox WHERE \"Empfaenger\" LIKE '%.MitgliedschaftAnlegen'"));
        Assert.Equal(0L, await ablauf.WertAsync<long>("SELECT count(*) FROM freizeiten.outbox_zustellung WHERE \"ErledigtAm\" IS NULL"));

        // Jonas arbeitet jetzt mit: Die Startseite der Freizeit lädt.
        Assert.Equal(HttpStatusCode.OK, (await ablauf.JonasClient.GetAsync($"/api/freizeiten/{ablauf.FreizeitId}", AnmeldeAblauf.Abbruch)).StatusCode);
    }
}
