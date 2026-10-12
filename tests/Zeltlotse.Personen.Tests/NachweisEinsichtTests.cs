using System.Text;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Freizeiten.Contracts.Events;
using Zeltlotse.Personen.Freizeitrollen;
using Zeltlotse.Personen.Nachweise;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.Testing;
using EreignisArt = Zeltlotse.Personen.Contracts.Events.NachweisArt;

namespace Zeltlotse.Personen.Tests;

/// <summary>Hilfen der Einsicht-Tests: Freizeit-Rolle wie der Zustelldienst zustellen (AD-4, Mandant aus dem Umschlag).</summary>
internal static class EinsichtHilfe
{
    private static long _folgenummer;

    /// <summary>Wie die Freizeit: Datensatz ist die Freizeit, jede Rolle mit höherer Folgenummer (die Inbox verwirft Älteres).</summary>
    public static async Task RolleAsync(NachweisTestBasis basis, Guid org, Guid freizeit, Guid person, FreizeitRolleArt rolle = FreizeitRolleArt.Leitung) =>
        await basis.RolleZustellenAsync(
            new Umschlag(Guid.CreateVersion7(), DateTimeOffset.UtcNow, org, freizeit, Interlocked.Increment(ref _folgenummer)),
            new FreizeitRolleGeaendert(freizeit, person, rolle));
}

/// <summary>
/// Einsicht in Nachweise (FR-10, AD-9, AD-28) mit künstlicher Uhr: automatisch mit jeder Freizeit-Rolle (idempotent), von Hand
/// durch Leitung oder Org-Admin, Freigabe für 14 Tage, Prüfung übernehmen (genau eine prüfende Person), Bestätigen mit
/// Ereignis, Ablehnen mit verschlüsseltem Hinweis, Abschluss und neue Anfrage nach einer Ablehnung, Zeilensperre.
/// </summary>
public sealed class NachweisEinsichtTests(PostgresContainer postgres) : NachweisTestBasis(postgres)
{
    private static readonly Guid OrgA = Guid.Parse("0199d000-0000-7000-8000-00000000a0a0");
    private static readonly Guid OrgB = Guid.Parse("0199d000-0000-7000-8000-00000000b0b0");
    private static readonly Guid Zeltlager = Guid.Parse("0199d000-0000-7000-8000-0000000f0001");
    private static readonly Guid Freizeit2 = Guid.Parse("0199d000-0000-7000-8000-0000000f0002");

    // ---------- automatisch ----------

    [Fact]
    public async Task Jede_Freizeit_Rolle_stellt_genau_eine_offene_Einsicht_ohne_pruefende_Person_sicher()
    {
        await EinsichtHilfe.RolleAsync(this, OrgA, Zeltlager, Jonas, FreizeitRolleArt.Team);
        await EinsichtHilfe.RolleAsync(this, OrgA, Zeltlager, Jonas, FreizeitRolleArt.Team);
        await EinsichtHilfe.RolleAsync(this, OrgA, Freizeit2, Jonas);

        var einsicht = Assert.Single(await LesenAsync(db => db.Einsichten.ToListAsync(Abbruch), OrgA));
        Assert.Equal(
            (OrgA, Jonas, (Guid?)Zeltlager, (Guid?)null, (Guid?)null, EinsichtStatus.Angefragt, Factory.Uhr.GetUtcNow()),
            (einsicht.OrganisationId, einsicht.PersonId, einsicht.FreizeitId, einsicht.AngefragtVon, einsicht.PruefendePersonId, einsicht.Status, einsicht.AngefragtAm));
        Assert.Empty(await LesenAsync(db => db.Einsichten.ToListAsync(Abbruch), OrgB));
    }

    [Fact]
    public async Task Nach_dem_Abschluss_fragt_eine_weitere_Rolle_nicht_erneut_automatisch_an()
    {
        await RollenAsync();
        await AlleEinsichtenAbschliessenAsync();

        await EinsichtHilfe.RolleAsync(this, OrgA, Freizeit2, Jonas);

        var einsichten = await LesenAsync(db => db.Einsichten.Where(e => e.PersonId == Jonas).ToListAsync(Abbruch), OrgA);
        Assert.Equal(EinsichtStatus.Abgeschlossen, Assert.Single(einsichten).Status);
        Assert.Equal(
            Assert.Single(einsichten).Id,
            await SendenAsync(new AutomatischeEinsichtSicherstellen(OrgA, Freizeit2, Jonas), OrgA));
        Assert.Single(await LesenAsync(db => db.Einsichten.Where(e => e.PersonId == Jonas).ToListAsync(Abbruch), OrgA));
    }

    [Fact]
    public async Task Je_Organisation_eine_eigene_offene_Einsicht()
    {
        await EinsichtHilfe.RolleAsync(this, OrgA, Zeltlager, Jonas);
        await EinsichtHilfe.RolleAsync(this, OrgB, Freizeit2, Jonas);

        Assert.Single(await LesenAsync(db => db.Einsichten.ToListAsync(Abbruch), OrgA));
        Assert.Single(await LesenAsync(db => db.Einsichten.ToListAsync(Abbruch), OrgB));
    }

    // ---------- von Hand ----------

    [Fact]
    public async Task Leitung_fragt_fuer_ein_Teammitglied_an_und_ist_sofort_gebunden_ein_zweites_Mal_kommt_dieselbe()
    {
        await RollenAsync();
        await AlleEinsichtenAbschliessenAsync();

        var neu = Assert.IsType<EinsichtAnfrageErgebnis.Neu>(await SendenAsync(new EinsichtAnfragenCommand(OrgA, Zeltlager, Miriam, Jonas), OrgA));
        var vorhanden = Assert.IsType<EinsichtAnfrageErgebnis.Vorhanden>(await SendenAsync(new EinsichtAnfragenCommand(OrgA, Zeltlager, Tobias, Jonas), OrgA));

        Assert.Equal(neu.EinsichtId, vorhanden.EinsichtId);
        var einsicht = await EinsichtAsync(neu.EinsichtId);
        Assert.Equal(((Guid?)Zeltlager, (Guid?)Miriam, (Guid?)Miriam), (einsicht.FreizeitId, einsicht.AngefragtVon, einsicht.PruefendePersonId));
    }

    [Fact]
    public async Task Anfrage_von_Hand_nicht_fuer_sich_selbst_und_nicht_ohne_Rolle()
    {
        await RollenAsync();
        await EinsichtHilfe.RolleAsync(this, OrgA, Freizeit2, Lea, FreizeitRolleArt.Team);

        Assert.IsType<EinsichtAnfrageErgebnis.NichtGefunden>(await SendenAsync(new EinsichtAnfragenCommand(OrgA, Zeltlager, Miriam, Miriam), OrgA));
        Assert.IsType<EinsichtAnfrageErgebnis.NichtGefunden>(await SendenAsync(new EinsichtAnfragenCommand(OrgA, Zeltlager, Miriam, Lea), OrgA));
        Assert.IsType<EinsichtAnfrageErgebnis.NichtGefunden>(await SendenAsync(new EinsichtAnfragenCommand(OrgA, null, Andrea, Andrea), OrgA));
        Assert.IsType<EinsichtAnfrageErgebnis.NichtGefunden>(await SendenAsync(new EinsichtAnfragenCommand(OrgB, null, Andrea, Jonas), OrgB));
        Assert.IsType<EinsichtAnfrageErgebnis.NichtGefunden>(await SendenAsync(new EinsichtAnfragenCommand(OrgA, null, Andrea, null), OrgA));
    }

    [Fact]
    public async Task Org_Admin_fragt_ohne_Freizeit_an_und_ist_gebunden()
    {
        await RollenAsync();
        await AlleEinsichtenAbschliessenAsync();

        var neu = Assert.IsType<EinsichtAnfrageErgebnis.Neu>(await SendenAsync(new EinsichtAnfragenCommand(OrgA, null, Andrea, Jonas), OrgA));

        var einsicht = await EinsichtAsync(neu.EinsichtId);
        Assert.Equal(((Guid?)null, (Guid?)Andrea, (Guid?)Andrea), (einsicht.FreizeitId, einsicht.AngefragtVon, einsicht.PruefendePersonId));
    }

    // ---------- Freigeben ----------

    [Fact]
    public async Task Freigeben_fuer_14_Tage_mit_je_einem_Posten_je_Nachweis_mit_Datei()
    {
        var einsicht = await AutomatischAsync();
        var mitDatei = await NachweisAsync(EreignisArt.Fuehrungszeugnis, Pdf);
        var auchMitDatei = await NachweisAsync(EreignisArt.ErsteHilfe, Png);
        await NachweisAsync(EreignisArt.Juleica, datei: null);

        var freigabe = Assert.IsType<EinsichtFreigabe.Freigegeben>(await SendenAsync(new EinsichtFreigeben(Jonas, einsicht)));

        Assert.Equal(Factory.Uhr.GetUtcNow() + TimeSpan.FromDays(14), freigabe.Bis);
        var gelesen = await EinsichtAsync(einsicht);
        Assert.Equal((EinsichtStatus.Freigegeben, (DateTimeOffset?)Factory.Uhr.GetUtcNow(), (DateTimeOffset?)freigabe.Bis), (gelesen.Status, gelesen.FreigegebenAm, gelesen.FreigegebenBis));
        Assert.Equal(
            [(mitDatei, EreignisArt.Fuehrungszeugnis, PostenErgebnisArt.Offen), (auchMitDatei, EreignisArt.ErsteHilfe, PostenErgebnisArt.Offen)],
            gelesen.Posten.OrderBy(p => p.Art).Select(p => (p.NachweisId, p.Art, p.Ergebnis)));
    }

    [Fact]
    public async Task Freigeben_ohne_Datei_fremd_oder_abgeschlossen_geht_nicht()
    {
        var einsicht = await AutomatischAsync();
        await NachweisAsync(EreignisArt.Juleica, datei: null);

        Assert.IsType<EinsichtFreigabe.OhneDatei>(await SendenAsync(new EinsichtFreigeben(Jonas, einsicht)));
        Assert.IsType<EinsichtFreigabe.NichtGefunden>(await SendenAsync(new EinsichtFreigeben(Lea, einsicht)));
        Assert.IsType<EinsichtFreigabe.NichtGefunden>(await SendenAsync(new EinsichtFreigeben(Jonas, Guid.CreateVersion7())));
        Assert.Equal(EinsichtStatus.Angefragt, (await EinsichtAsync(einsicht)).Status);

        await NachweisAsync(EreignisArt.ErsteHilfe, Png);
        await FreigebenUndUebernehmenAsync(einsicht);
        Assert.True(await SendenAsync(new NachweisEntscheiden(Miriam, einsicht, await NachweisIdAsync(EreignisArt.ErsteHilfe), true, null), OrgA));

        Assert.Equal(EinsichtStatus.Abgeschlossen, (await EinsichtAsync(einsicht)).Status);
        Assert.IsType<EinsichtFreigabe.NichtGefunden>(await SendenAsync(new EinsichtFreigeben(Jonas, einsicht)));
    }

    [Fact]
    public async Task Freigeben_bei_noch_gueltiger_Freigabe_laeuft_schon()
    {
        var einsicht = await AutomatischAsync();
        await NachweisAsync(EreignisArt.Fuehrungszeugnis, Pdf);
        var erste = Assert.IsType<EinsichtFreigabe.Freigegeben>(await SendenAsync(new EinsichtFreigeben(Jonas, einsicht)));
        Factory.Uhr.Advance(TimeSpan.FromDays(14));

        Assert.IsType<EinsichtFreigabe.LaeuftSchon>(await SendenAsync(new EinsichtFreigeben(Jonas, einsicht)));
        Assert.Equal((DateTimeOffset?)erste.Bis, (await EinsichtAsync(einsicht)).FreigegebenBis);
    }

    [Fact]
    public async Task Nach_Ablauf_bleibt_die_Einsicht_offen_Anfragen_liefert_sie_erneutes_Freigeben_laesst_Entschiedenes_entschieden()
    {
        var einsicht = await AutomatischAsync();
        var fz = await NachweisAsync(EreignisArt.Fuehrungszeugnis, Pdf);
        var eh = await NachweisAsync(EreignisArt.ErsteHilfe, Png);
        await FreigebenUndUebernehmenAsync(einsicht);
        Assert.True(await SendenAsync(new NachweisEntscheiden(Miriam, einsicht, eh, true, null), OrgA));

        Factory.Uhr.Advance(TimeSpan.FromDays(14));
        Assert.True((await EinsichtAsync(einsicht)).FreigabeGueltig(Factory.Uhr.GetUtcNow()));
        Factory.Uhr.Advance(TimeSpan.FromMinutes(1));
        var abgelaufen = await EinsichtAsync(einsicht);
        Assert.False(abgelaufen.FreigabeGueltig(Factory.Uhr.GetUtcNow()));
        Assert.True(abgelaufen.FreigabeAbgelaufen(Factory.Uhr.GetUtcNow()));
        Assert.True(abgelaufen.IstOffen);
        Assert.False(await SendenAsync(new NachweisEntscheiden(Miriam, einsicht, fz, true, null), OrgA));
        Assert.IsType<PruefungUebernahme.NichtFreigegeben>(await SendenAsync(new EinsichtPruefungUebernehmen(Zeltlager, Miriam, einsicht), OrgA));

        // Von Hand angefragt kommt dieselbe offene Einsicht zurück, es entsteht keine zweite.
        Assert.Equal(
            new EinsichtAnfrageErgebnis.Vorhanden(einsicht),
            await SendenAsync(new EinsichtAnfragenCommand(OrgA, Zeltlager, Tobias, Jonas), OrgA));
        Assert.Single(await LesenAsync(db => db.Einsichten.Where(e => e.PersonId == Jonas).ToListAsync(Abbruch), OrgA));

        var erneut = Assert.IsType<EinsichtFreigabe.Freigegeben>(await SendenAsync(new EinsichtFreigeben(Jonas, einsicht)));

        Assert.Equal(Factory.Uhr.GetUtcNow() + TimeSpan.FromDays(14), erneut.Bis);
        var gelesen = await EinsichtAsync(einsicht);
        Assert.Equal(
            [(fz, PostenErgebnisArt.Offen), (eh, PostenErgebnisArt.Bestaetigt)],
            gelesen.Posten.OrderBy(p => p.Art).Select(p => (p.NachweisId, p.Ergebnis)));
        Assert.Equal((Guid?)Miriam, gelesen.PruefendePersonId);
    }

    // ---------- Prüfen übernehmen ----------

    [Fact]
    public async Task Genau_eine_Leitung_uebernimmt_die_Pruefung()
    {
        var einsicht = await AutomatischAsync();
        await NachweisAsync(EreignisArt.Fuehrungszeugnis, Pdf);

        Assert.IsType<PruefungUebernahme.NichtFreigegeben>(await SendenAsync(new EinsichtPruefungUebernehmen(Zeltlager, Miriam, einsicht), OrgA));
        Assert.Null((await EinsichtAsync(einsicht)).PruefendePersonId);
        Assert.IsType<EinsichtFreigabe.Freigegeben>(await SendenAsync(new EinsichtFreigeben(Jonas, einsicht)));

        Assert.IsType<PruefungUebernahme.NichtGefunden>(await SendenAsync(new EinsichtPruefungUebernehmen(Zeltlager, Jonas, einsicht), OrgA));
        Assert.IsType<PruefungUebernahme.NichtGefunden>(await SendenAsync(new EinsichtPruefungUebernehmen(null, Jonas, einsicht), OrgA));
        // Jonas hat in Freizeit 2 keine Rolle: deren Leitung kann nicht übernehmen.
        Assert.IsType<PruefungUebernahme.NichtGefunden>(await SendenAsync(new EinsichtPruefungUebernehmen(Freizeit2, Miriam, einsicht), OrgA));
        Assert.IsType<PruefungUebernahme.Gebunden>(await SendenAsync(new EinsichtPruefungUebernehmen(Zeltlager, Miriam, einsicht), OrgA));
        Assert.IsType<PruefungUebernahme.Gebunden>(await SendenAsync(new EinsichtPruefungUebernehmen(Zeltlager, Miriam, einsicht), OrgA));
        Assert.IsType<PruefungUebernahme.NichtGefunden>(await SendenAsync(new EinsichtPruefungUebernehmen(Zeltlager, Tobias, einsicht), OrgA));
        Assert.IsType<PruefungUebernahme.NichtGefunden>(await SendenAsync(new EinsichtPruefungUebernehmen(null, Andrea, einsicht), OrgA));
        Assert.IsType<PruefungUebernahme.NichtGefunden>(await SendenAsync(new EinsichtPruefungUebernehmen(Zeltlager, Miriam, einsicht), OrgB));

        Assert.Equal((Guid?)Miriam, (await EinsichtAsync(einsicht)).PruefendePersonId);
    }

    [Fact]
    public async Task Uebernehmen_ueber_eine_andere_Freizeit_mit_Rolle_der_Person_oder_ueber_die_Organisation()
    {
        var einsicht = await AutomatischAsync();
        await NachweisAsync(EreignisArt.Fuehrungszeugnis, Pdf);
        await EinsichtHilfe.RolleAsync(this, OrgA, Freizeit2, Jonas, FreizeitRolleArt.Team);
        await EinsichtHilfe.RolleAsync(this, OrgA, Freizeit2, Lea);
        Assert.Single(await LesenAsync(db => db.Einsichten.Where(e => e.PersonId == Jonas).ToListAsync(Abbruch), OrgA));
        await SendenAsync(new EinsichtFreigeben(Jonas, einsicht));

        Assert.IsType<PruefungUebernahme.Gebunden>(await SendenAsync(new EinsichtPruefungUebernehmen(Freizeit2, Lea, einsicht), OrgA));
        Assert.Equal((Guid?)Lea, (await EinsichtAsync(einsicht)).PruefendePersonId);
        Assert.IsType<PruefungUebernahme.NichtGefunden>(await SendenAsync(new EinsichtPruefungUebernehmen(Zeltlager, Miriam, einsicht), OrgA));

        // Org-Admin über die Org-Route: Miriams automatische Einsicht (einzige Leitung ihrer Freizeit, die sie selbst nicht prüft).
        await NachweisFuerAsync(Miriam, EreignisArt.Juleica, Pdf);
        var miriams = (await LesenAsync(db => db.Einsichten.SingleAsync(e => e.PersonId == Miriam, Abbruch), OrgA)).Id;
        Assert.IsType<EinsichtFreigabe.Freigegeben>(await SendenAsync(new EinsichtFreigeben(Miriam, miriams)));
        Assert.IsType<PruefungUebernahme.NichtGefunden>(await SendenAsync(new EinsichtPruefungUebernehmen(null, Miriam, miriams), OrgA));
        Assert.IsType<PruefungUebernahme.Gebunden>(await SendenAsync(new EinsichtPruefungUebernehmen(null, Andrea, miriams), OrgA));
        Assert.Equal((Guid?)Andrea, (await EinsichtAsync(miriams)).PruefendePersonId);
    }

    [Fact]
    public async Task Gleichzeitiges_Uebernehmen_bindet_genau_eine_Leitung()
    {
        var einsicht = await AutomatischAsync();
        await NachweisAsync(EreignisArt.Fuehrungszeugnis, Pdf);
        await SendenAsync(new EinsichtFreigeben(Jonas, einsicht));

        var ergebnisse = await Task.WhenAll(new[] { Miriam, Tobias, Miriam, Tobias }
            .Select(p => Task.Run(() => SendenAsync(new EinsichtPruefungUebernehmen(Zeltlager, p, einsicht), OrgA))));

        var gebunden = (await EinsichtAsync(einsicht)).PruefendePersonId;
        Assert.NotNull(gebunden);
        Assert.Equal(2, ergebnisse.Count(e => e is PruefungUebernahme.Gebunden));
        Assert.Equal(2, ergebnisse.Count(e => e is PruefungUebernahme.NichtGefunden));
    }

    // ---------- Entscheiden ----------

    [Fact]
    public async Task Bestaetigen_legt_das_Ereignis_genau_einmal_ab_und_der_Posten_ist_danach_entschieden()
    {
        var einsicht = await AutomatischAsync();
        var eh = await NachweisAsync(EreignisArt.ErsteHilfe, Png, new DateOnly(2025, 4, 15));
        var fz = await NachweisAsync(EreignisArt.Fuehrungszeugnis, Pdf);
        await FreigebenUndUebernehmenAsync(einsicht);
        Assert.False(await SendenAsync(new NachweisEntscheiden(Tobias, einsicht, eh, true, null), OrgA));

        Assert.True(await SendenAsync(new NachweisEntscheiden(Miriam, einsicht, eh, true, null), OrgA));
        Assert.False(await SendenAsync(new NachweisEntscheiden(Miriam, einsicht, eh, true, null), OrgA));
        Assert.False(await SendenAsync(new NachweisEntscheiden(Miriam, einsicht, Guid.CreateVersion7(), true, null), OrgA));

        var gelesen = await EinsichtAsync(einsicht);
        Assert.Equal(EinsichtStatus.Freigegeben, gelesen.Status);
        var posten = gelesen.Posten.Single(p => p.NachweisId == eh);
        Assert.Equal((PostenErgebnisArt.Bestaetigt, (DateTimeOffset?)Factory.Uhr.GetUtcNow()), (posten.Ergebnis, posten.EntschiedenAm));
        Assert.Equal(PostenErgebnisArt.Offen, gelesen.Posten.Single(p => p.NachweisId == fz).Ergebnis);
        Assert.Equal(1L, await WertAsync<long>("SELECT count(*) FROM personen.outbox WHERE \"Typ\" LIKE '%.NachweisEingesehen'"));
        var inhalt = await WertAsync<string>("SELECT \"Inhalt\" FROM personen.outbox");
        Assert.Contains(eh.ToString(), inhalt, StringComparison.Ordinal);
        Assert.Contains("2025-04-15", inhalt, StringComparison.Ordinal);
        Assert.Contains(Miriam.ToString(), inhalt, StringComparison.Ordinal);
        Assert.Equal(OrgA, await WertAsync<Guid>("SELECT \"OrganisationId\" FROM personen.outbox"));
        Assert.Equal(einsicht, await WertAsync<Guid>("SELECT \"DatensatzId\" FROM personen.outbox"));
    }

    [Fact]
    public async Task Ablehnen_verlangt_einen_Hinweis_von_1_bis_500_Zeichen()
    {
        var einsicht = await AutomatischAsync();
        var fz = await NachweisAsync(EreignisArt.Fuehrungszeugnis, Pdf);
        await FreigebenUndUebernehmenAsync(einsicht);

        foreach (var hinweis in new[] { null, "   ", new string('a', 501) })
        {
            var fehler = await Assert.ThrowsAsync<ValidierungsAusnahme>(() => SendenAsync(new NachweisEntscheiden(Miriam, einsicht, fz, false, hinweis), OrgA));
            Assert.Equal(
                [hinweis is { Length: 501 } ? "Der Hinweis ist zu lang (höchstens 500 Zeichen)." : "Hinweis fehlt."],
                fehler.Fehler["hinweis"]);
        }

        Assert.True(await SendenAsync(new NachweisEntscheiden(Miriam, einsicht, fz, false, " " + new string('a', 500) + " "), OrgA));
    }

    [Fact]
    public async Task Ablehnen_des_letzten_Postens_schliesst_ab_und_fragt_neu_an_der_Hinweis_ist_verschluesselt()
    {
        var einsicht = await AutomatischAsync();
        var fz = await NachweisAsync(EreignisArt.Fuehrungszeugnis, Pdf);
        var eh = await NachweisAsync(EreignisArt.ErsteHilfe, Png);
        await FreigebenUndUebernehmenAsync(einsicht);
        Assert.True(await SendenAsync(new NachweisEntscheiden(Miriam, einsicht, eh, true, null), OrgA));
        Factory.Uhr.Advance(TimeSpan.FromHours(2));

        Assert.True(await SendenAsync(new NachweisEntscheiden(Miriam, einsicht, fz, false, " Datum passt nicht "), OrgA));

        var alt = await EinsichtAsync(einsicht);
        Assert.Equal((EinsichtStatus.Abgeschlossen, (DateTimeOffset?)Factory.Uhr.GetUtcNow()), (alt.Status, alt.AbgeschlossenAm));
        var neu = Assert.Single(await LesenAsync(db => db.Einsichten.Where(e => e.PersonId == Jonas && e.Id != einsicht).ToListAsync(Abbruch), OrgA));
        Assert.Equal(
            (OrgA, Jonas, (Guid?)Zeltlager, (Guid?)Miriam, (Guid?)Miriam, EinsichtStatus.Angefragt),
            (neu.OrganisationId, neu.PersonId, neu.FreizeitId, neu.AngefragtVon, neu.PruefendePersonId, neu.Status));
        Assert.True(neu.IstOffen);
        Assert.Equal([(short)EreignisArt.Fuehrungszeugnis], neu.NurArten!);

        var roh = await WertAsync<byte[]>($"SELECT \"Ablehnungshinweis\" FROM personen.nachweis WHERE \"Id\" = '{fz}'");
        Assert.DoesNotContain("Datum passt", Encoding.UTF8.GetString(roh), StringComparison.Ordinal);
        var nachweis = await LesenAsync(db => db.Nachweise.SingleAsync(n => n.Id == fz, Abbruch));
        await using var scope = Factory.Services.CreateAsyncScope();
        Assert.Equal("Datum passt nicht", await scope.ServiceProvider.GetRequiredService<Zeltlotse.SharedKernel.Datenschutz.IVerschluesselung>()
            .EntschluesselnAsync(nachweis.Ablehnungshinweis!, Abbruch));
        Assert.Equal(Jonas, nachweis.Ablehnungshinweis!.PersonId);
        Assert.Equal(1L, await WertAsync<long>("SELECT count(*) FROM personen.outbox WHERE \"Typ\" LIKE '%.NachweisEingesehen'"));

        // Die Neu-Anfrage zeigt nur das Abgelehnte: Freigeben legt nur den Posten des Führungszeugnisses an.
        Assert.IsType<NachweisDateiErgebnis.Hochgeladen>(await SendenAsync(new NachweisDateiHochladen(Jonas, fz, Png)));
        Assert.IsType<EinsichtFreigabe.Freigegeben>(await SendenAsync(new EinsichtFreigeben(Jonas, neu.Id)));
        Assert.Equal([(fz, EreignisArt.Fuehrungszeugnis)], (await EinsichtAsync(neu.Id)).Posten.Select(p => (p.NachweisId, p.Art)));
        Assert.IsType<PruefungUebernahme.Gebunden>(await SendenAsync(new EinsichtPruefungUebernehmen(Zeltlager, Miriam, neu.Id), OrgA));
    }

    [Fact]
    public async Task Gleichzeitiges_Entscheiden_derselben_Einsicht_laeuft_nacheinander()
    {
        var einsicht = await AutomatischAsync();
        var fz = await NachweisAsync(EreignisArt.Fuehrungszeugnis, Pdf);
        var eh = await NachweisAsync(EreignisArt.ErsteHilfe, Png);
        var ju = await NachweisAsync(EreignisArt.Juleica, Pdf, new DateOnly(2024, 5, 20));
        await FreigebenUndUebernehmenAsync(einsicht);

        var ergebnisse = await Task.WhenAll(new[] { fz, eh, ju, fz, eh, ju }
            .Select(n => Task.Run(() => SendenAsync(new NachweisEntscheiden(Miriam, einsicht, n, true, null), OrgA))));

        Assert.Equal(3, ergebnisse.Count(e => e));
        var gelesen = await EinsichtAsync(einsicht);
        Assert.Equal(EinsichtStatus.Abgeschlossen, gelesen.Status);
        Assert.All(gelesen.Posten, p => Assert.Equal(PostenErgebnisArt.Bestaetigt, p.Ergebnis));
        Assert.Equal(3L, await WertAsync<long>("SELECT count(*) FROM personen.outbox WHERE \"Typ\" LIKE '%.NachweisEingesehen'"));
        Assert.Equal(3L, await WertAsync<long>("SELECT max(\"Folgenummer\") FROM personen.outbox"));
    }

    // ---------- Hilfen ----------

    /// <summary>Jonas als Team, Miriam und Tobias als Leitung im Zeltlager von Org A; liefert Jonas' automatische Einsicht.</summary>
    private async Task<Guid> AutomatischAsync()
    {
        await RollenAsync();
        return (await LesenAsync(db => db.Einsichten.SingleAsync(e => e.PersonId == Jonas, Abbruch), OrgA)).Id;
    }

    private async Task RollenAsync()
    {
        await EinsichtHilfe.RolleAsync(this, OrgA, Zeltlager, Jonas, FreizeitRolleArt.Team);
        await EinsichtHilfe.RolleAsync(this, OrgA, Zeltlager, Miriam);
        await EinsichtHilfe.RolleAsync(this, OrgA, Zeltlager, Tobias);
    }

    /// <summary>Schließt Jonas' automatische Einsicht ab (Datei, freigeben, Miriam bestätigt), damit eine Anfrage neu ist.</summary>
    private async Task AlleEinsichtenAbschliessenAsync()
    {
        var einsicht = (await LesenAsync(db => db.Einsichten.SingleAsync(e => e.PersonId == Jonas, Abbruch), OrgA)).Id;
        var id = await NachweisAsync(EreignisArt.Praeventionsschulung, Pdf);
        await FreigebenUndUebernehmenAsync(einsicht);
        Assert.True(await SendenAsync(new NachweisEntscheiden(Miriam, einsicht, id, true, null), OrgA));
    }

    private async Task FreigebenUndUebernehmenAsync(Guid einsicht)
    {
        Assert.IsType<EinsichtFreigabe.Freigegeben>(await SendenAsync(new EinsichtFreigeben(Jonas, einsicht)));
        Assert.IsType<PruefungUebernahme.Gebunden>(await SendenAsync(new EinsichtPruefungUebernehmen(Zeltlager, Miriam, einsicht), OrgA));
    }

    private Task<Guid> NachweisAsync(EreignisArt art, byte[]? datei, DateOnly? datum = null) => NachweisFuerAsync(Jonas, art, datei, datum);

    private async Task<Guid> NachweisFuerAsync(Guid person, EreignisArt art, byte[]? datei, DateOnly? datum = null)
    {
        var id = Assert.IsType<NachweisAnlage.Angelegt>(await SendenAsync(new NachweisAnlegen(person, art, datum ?? new DateOnly(2025, 11, 3)))).NachweisId;
        if (datei is not null)
        {
            Assert.IsType<NachweisDateiErgebnis.Hochgeladen>(await SendenAsync(new NachweisDateiHochladen(person, id, datei)));
        }

        return id;
    }

    private Task<Guid> NachweisIdAsync(EreignisArt art) =>
        LesenAsync(db => db.Nachweise.Where(n => n.PersonId == Jonas && n.Art == art).Select(n => n.Id).SingleAsync(Abbruch));

    private Task<NachweisEinsicht> EinsichtAsync(Guid id) =>
        LesenAsync(db => db.Einsichten.Include(e => e.Posten).SingleAsync(e => e.Id == id, Abbruch), OrgA);
}
