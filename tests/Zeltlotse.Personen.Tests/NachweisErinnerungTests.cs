using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Freizeiten.Contracts.Events;
using Zeltlotse.Personen.Nachweise;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mail;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.Testing;

namespace Zeltlotse.Personen.Tests;

/// <summary>
/// Erinnern an fehlende Nachweise (FR-11, AD-13, Story 1-6) mit künstlicher Uhr: eine Zeile je Freizeit und Person, höchstens
/// einmal je Kalendertag (Europe/Berlin), offene Einsicht mit der Logik von „Einsicht anfragen“ sichergestellt, Mail-Auftrag
/// nur mit Kennungen in der Outbox; die Mail entsteht erst beim Versand.
/// </summary>
public sealed class NachweisErinnerungTests(PostgresContainer postgres) : NachweisTestBasis(postgres)
{
    private static readonly Guid OrgA = Guid.Parse("0199d000-0000-7000-8000-00000000a0a1");
    private static readonly Guid Zeltlager = Guid.Parse("0199d000-0000-7000-8000-0000000f0011");
    private static readonly Guid Freizeit2 = Guid.Parse("0199d000-0000-7000-8000-0000000f0012");

    private async Task RollenAsync()
    {
        await EinsichtHilfe.RolleAsync(this, OrgA, Zeltlager, Miriam);
        await EinsichtHilfe.RolleAsync(this, OrgA, Zeltlager, Jonas, FreizeitRolleArt.Team);
        await EinsichtHilfe.RolleAsync(this, OrgA, Freizeit2, Lea, FreizeitRolleArt.Team);
    }

    private Task<ErinnerungErgebnis> ErinnernAsync(Guid? person, Guid von = default, Guid? freizeit = null) =>
        SendenAsync(new NachweisErinnern(OrgA, freizeit ?? Zeltlager, von == default ? Miriam : von, person), OrgA);

    [Fact]
    public async Task Erinnern_ohne_offene_Einsicht_fragt_an_speichert_die_Erinnerung_und_merkt_genau_eine_Mail_vor()
    {
        await RollenAsync();
        await WertAsync<object>("DELETE FROM personen.nachweis_einsicht");

        var erinnert = Assert.IsType<ErinnerungErgebnis.Erinnert>(await ErinnernAsync(Jonas));

        Assert.Equal(Factory.Uhr.GetUtcNow(), erinnert.ErinnertAm);
        var erinnerung = Assert.Single(await LesenAsync(db => db.Erinnerungen.ToListAsync(Abbruch), OrgA));
        Assert.Equal((OrgA, Zeltlager, Jonas, Miriam, Factory.Uhr.GetUtcNow()), (erinnerung.OrganisationId, erinnerung.FreizeitId, erinnerung.PersonId, erinnerung.ErinnertVon, erinnerung.ErinnertAm));
        var einsicht = Assert.Single(await LesenAsync(db => db.Einsichten.ToListAsync(Abbruch), OrgA));
        Assert.Equal((Jonas, (Guid?)Zeltlager, (Guid?)Miriam, (Guid?)Miriam, EinsichtStatus.Angefragt), (einsicht.PersonId, einsicht.FreizeitId, einsicht.AngefragtVon, einsicht.PruefendePersonId, einsicht.Status));

        var inhalt = await WertAsync<string>($"SELECT \"Inhalt\"::text FROM personen.outbox WHERE \"Typ\" = '{typeof(NachweisErinnerungMail).FullName}'");
        using var json = JsonDocument.Parse(inhalt);
        Assert.Equal([("personId", Jonas.ToString()), ("erinnertVon", Miriam.ToString())], json.RootElement.EnumerateObject().Select(p => (p.Name, p.Value.GetString())));
        Assert.Equal(MailAuftraege.Empfaenger, await WertAsync<string>(
            $"SELECT z.\"Empfaenger\" FROM personen.outbox_zustellung z JOIN personen.outbox o ON o.\"Id\" = z.\"EreignisId\" WHERE o.\"Typ\" = '{typeof(NachweisErinnerungMail).FullName}'"));
        Assert.Equal(1L, await WertAsync<long>("SELECT count(*) FROM personen.outbox_zustellung WHERE \"Empfaenger\" = 'Zeltlotse.Mail'"));
    }

    [Fact]
    public async Task Eine_offene_automatische_Einsicht_bleibt_unveraendert()
    {
        await RollenAsync();
        var vorher = await LesenAsync(db => db.Einsichten.SingleAsync(e => e.PersonId == Jonas, Abbruch), OrgA);

        Assert.IsType<ErinnerungErgebnis.Erinnert>(await ErinnernAsync(Jonas));

        var nachher = Assert.Single(await LesenAsync(db => db.Einsichten.Where(e => e.PersonId == Jonas).ToListAsync(Abbruch), OrgA));
        Assert.Equal((vorher.Id, (Guid?)null, (Guid?)null), (nachher.Id, nachher.AngefragtVon, nachher.PruefendePersonId));
    }

    [Fact]
    public async Task Hoechstens_einmal_je_Kalendertag_in_Berlin_und_eine_Zeile_je_Freizeit_und_Person()
    {
        await RollenAsync();
        Assert.IsType<ErinnerungErgebnis.Erinnert>(await ErinnernAsync(Jonas));

        // 08:00 UTC + 13:59 = 21:59 UTC = 23:59 MESZ: noch derselbe Tag, auch für die zweite Leitung.
        Factory.Uhr.Advance(TimeSpan.FromHours(13) + TimeSpan.FromMinutes(59));
        await EinsichtHilfe.RolleAsync(this, OrgA, Zeltlager, Tobias);
        Assert.IsType<ErinnerungErgebnis.SchonHeute>(await ErinnernAsync(Jonas));
        Assert.IsType<ErinnerungErgebnis.SchonHeute>(await ErinnernAsync(Jonas, Tobias));

        Factory.Uhr.Advance(TimeSpan.FromMinutes(1));
        var zweite = Assert.IsType<ErinnerungErgebnis.Erinnert>(await ErinnernAsync(Jonas, Tobias));

        var erinnerung = Assert.Single(await LesenAsync(db => db.Erinnerungen.ToListAsync(Abbruch), OrgA));
        Assert.Equal((Tobias, zweite.ErinnertAm, 2L), (erinnerung.ErinnertVon, erinnerung.ErinnertAm, erinnerung.Folgenummer));
        Assert.Equal(2L, await WertAsync<long>($"SELECT count(*) FROM personen.outbox WHERE \"Typ\" = '{typeof(NachweisErinnerungMail).FullName}'"));
    }

    [Fact]
    public async Task Nicht_fuer_sich_selbst_nicht_ohne_Rolle_in_der_Freizeit_und_ohne_Person_ein_Feldfehler()
    {
        await RollenAsync();

        Assert.IsType<ErinnerungErgebnis.NichtGefunden>(await ErinnernAsync(Miriam));
        Assert.IsType<ErinnerungErgebnis.NichtGefunden>(await ErinnernAsync(Lea));
        Assert.IsType<ErinnerungErgebnis.NichtGefunden>(await ErinnernAsync(Guid.CreateVersion7()));
        var fehler = await Assert.ThrowsAsync<ValidierungsAusnahme>(() => ErinnernAsync(null));
        Assert.Equal(["Person fehlt."], fehler.Fehler["personId"]);

        Assert.Empty(await LesenAsync(db => db.Erinnerungen.ToListAsync(Abbruch), OrgA));
        Assert.Equal(0L, await WertAsync<long>("SELECT count(*) FROM personen.outbox_zustellung WHERE \"Empfaenger\" = 'Zeltlotse.Mail'"));
    }

    [Fact]
    public async Task Ohne_Konto_baut_die_Erinnerung_keine_Mail()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(OrgA);
        var aufbau = scope.ServiceProvider.GetRequiredService<IMailAufbau<NachweisErinnerungMail>>();

        Assert.Null(await aufbau.AufbauenAsync(new NachweisErinnerungMail(Jonas, Miriam), Abbruch));
    }

    [Fact]
    public void Text_nennt_die_Leitung_und_den_Link_ohne_Anrede_und_ohne_Angaben_zu_Nachweisen()
    {
        var text = ErinnerungsMail.Text("https://zeltlotse.example.org", "Miriam", "Zeltner");

        Assert.Equal("Erinnerung: Nachweise in Zeltlotse", ErinnerungsMail.Betreff);
        Assert.StartsWith("Miriam Zeltner aus der Leitung einer Freizeit bittet darum, die Nachweise in Zeltlotse zu ergänzen und die Anfrage zur Einsicht freizugeben:", text, StringComparison.Ordinal);
        Assert.Contains("https://zeltlotse.example.org/app/konto/nachweise", text, StringComparison.Ordinal);
        Assert.EndsWith("Diese Mail hat die Leitung ausgelöst.", text.TrimEnd(), StringComparison.Ordinal);
        Assert.DoesNotContain("Führungszeugnis", text, StringComparison.Ordinal);
    }
}
