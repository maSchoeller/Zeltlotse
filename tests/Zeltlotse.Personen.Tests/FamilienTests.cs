using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Freizeiten.Contracts.Events;
using Zeltlotse.Personen.Daten;
using Zeltlotse.Personen.Familien;
using Zeltlotse.Personen.Freigaben;
using Zeltlotse.Personen.Freizeitrollen;
using Zeltlotse.Personen.Profil;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Zugangslinks;
using Zeltlotse.Testing;

namespace Zeltlotse.Personen.Tests;

/// <summary>
/// Fachlogik von Familie und Profil-Übergabe gegen PostgreSQL mit künstlicher Uhr (FR-3, FR-4, AD-7, AD-11, AD-28): Kind
/// anlegen mit Personen-Schlüssel und Feldregeln, Volljährigkeit an der Berliner Tagesgrenze, Familien-Einladung (Ablauf,
/// einmal, Verwerfen, Mail-Bindung, 2 Eltern, andere Familie) und Profil-Übergabe (Volljährigkeit, Verwerfen, Annehmen mit
/// Löschen der leeren Person, Ereignis einmal, Konflikte, Nebenläufigkeit). Die Uhr der Factory steht am 01.07.2026.
/// </summary>
public sealed class FamilienTests(PostgresContainer postgres) : IAsyncLifetime
{
    private static readonly Guid Katrin = Guid.Parse("0199c000-0000-7000-8000-00000000000a");
    private static readonly Guid Daniel = Guid.Parse("0199c000-0000-7000-8000-00000000000b");
    private static readonly Guid Thomas = Guid.Parse("0199c000-0000-7000-8000-00000000000c");
    private static readonly Guid Paul = Guid.Parse("0199c000-0000-7000-8000-00000000000d");
    private static readonly Guid PaulKonto = Guid.Parse("0199c000-0000-7000-8000-0000000000e1");

    private const string ThomasMail = "thomas.muster@example.org";
    private const string PaulMail = "paul.muster@example.org";

    private static readonly DateOnly Emma = new(2017, 9, 2);

    private ZeltlotseApiFactory _factory = null!;
    private string _verbindung = null!;

    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _verbindung = await postgres.NeueDatenbankAsync();
        _factory = new ZeltlotseApiFactory(_verbindung);
        _ = _factory.CreateClient();
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    // ---------- Kind anlegen ----------

    [Fact]
    public async Task Kind_anlegen_legt_Familie_mit_Elternteil_Kind_und_Schluessel_in_einer_Transaktion_an()
    {
        var emma = await SendenAsync(new KindAnlegen(Katrin, " Emma ", " Muster ", Emma));
        var mia = await SendenAsync(new KindAnlegen(Katrin, "Mia", null, new DateOnly(2016, 5, 21)));

        var familie = await LesenAsync(db => db.Familien.SingleAsync(Abbruch));
        var elternteil = await LesenAsync(db => db.Elternteile.SingleAsync(Abbruch));
        Assert.Equal((familie.Id, Katrin), (elternteil.FamilieId, elternteil.PersonId));
        Assert.Equal(_factory.Uhr.GetUtcNow(), familie.AngelegtAm);
        var kind = await LesenAsync(db => db.Personen.SingleAsync(p => p.Id == emma, Abbruch));
        Assert.Equal(("Emma", "Muster", Emma, (Guid?)familie.Id), (kind.Vorname, kind.Nachname, kind.Geburtsdatum, kind.FamilieId));
        Assert.Null((await LesenAsync(db => db.Personen.SingleAsync(p => p.Id == mia, Abbruch))).Nachname);
        Assert.Equal(2, await LesenAsync(db => db.PersonenSchluessel.CountAsync(s => s.PersonId == emma || s.PersonId == mia, Abbruch)));
        Assert.Equal(0, await LesenAsync(db => db.Personen.CountAsync(p => p.Id == Katrin, Abbruch)));
    }

    [Fact]
    public async Task Scheitert_das_Anlegen_des_Kindes_gibt_es_weder_Familie_noch_Schluessel()
    {
        await AusfuehrenAsync("ALTER TABLE personen.person ADD CONSTRAINT keine_person CHECK (false) NOT VALID");

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => SendenAsync(new KindAnlegen(Katrin, "Emma", null, Emma)));

        Assert.Equal(0, await LesenAsync(db => db.Familien.CountAsync(Abbruch)));
        Assert.Equal(0, await LesenAsync(db => db.Elternteile.CountAsync(Abbruch)));
        Assert.Equal(0, await LesenAsync(db => db.PersonenSchluessel.CountAsync(Abbruch)));
    }

    public static TheoryData<string?, string?, string?, string, string> Ungueltig => new()
    {
        { null, null, "2017-09-02", "vorname", "Vorname fehlt." },
        { "   ", null, "2017-09-02", "vorname", "Vorname fehlt." },
        { new string('a', 101), null, "2017-09-02", "vorname", "Der Vorname ist zu lang (höchstens 100 Zeichen)." },
        { "Emma", new string('a', 101), "2017-09-02", "nachname", "Der Nachname ist zu lang (höchstens 100 Zeichen)." },
        { "Emma", null, null, "geburtsdatum", "Geburtsdatum fehlt." },
        { "Emma", null, "2026-07-02", "geburtsdatum", "Das Geburtsdatum liegt in der Zukunft." },
        { "Emma", null, "1899-12-31", "geburtsdatum", "Das Geburtsdatum ist ungültig." },
        { "Emma", null, "2008-07-01", "geburtsdatum", "Kinder ab 18 legen ein eigenes Konto an." },
    };

    [Theory]
    [MemberData(nameof(Ungueltig))]
    public async Task Kind_anlegen_prueft_jedes_Feld(string? vorname, string? nachname, string? geburtsdatum, string feld, string meldung)
    {
        var datum = geburtsdatum is null ? (DateOnly?)null : DateOnly.Parse(geburtsdatum, System.Globalization.CultureInfo.InvariantCulture);

        var fehler = await Assert.ThrowsAsync<ValidierungsAusnahme>(() => SendenAsync(new KindAnlegen(Katrin, vorname, nachname, datum)));

        Assert.Equal([feld], fehler.Fehler.Keys);
        Assert.Equal([meldung], fehler.Fehler[feld]);
        Assert.Equal(0, await LesenAsync(db => db.Familien.CountAsync(Abbruch)));
    }

    [Fact]
    public async Task Kind_anlegen_am_Tag_vor_dem_18_Geburtstag_gelingt_und_genau_100_Zeichen_auch()
    {
        var kind = await SendenAsync(new KindAnlegen(Katrin, new string('a', 100), new string('b', 100), new DateOnly(2008, 7, 2)));

        Assert.NotEqual(Guid.Empty, kind);
    }

    // ---------- Volljährigkeit ----------

    [Theory]
    [InlineData("2026-10-11T21:59:59Z", false)] // 23:59:59 in Berlin (Sommerzeit)
    [InlineData("2026-10-11T22:00:00Z", true)] // 00:00 am 12.10. in Berlin
    [InlineData("2026-10-12T21:59:59Z", true)]
    public void Volljaehrig_ab_dem_18_Geburtstag_als_Kalendertag_in_Berlin(string utc, bool erwartet)
    {
        var uhr = new FakeTimeProvider(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(erwartet, Volljaehrigkeit.Ist(new DateOnly(2008, 10, 12), Volljaehrigkeit.Heute(uhr)));
    }

    [Fact]
    public void Am_29_Februar_Geborene_sind_in_Nicht_Schaltjahren_ab_dem_28_Februar_volljaehrig()
    {
        Assert.False(Volljaehrigkeit.Ist(new DateOnly(2008, 2, 29), new DateOnly(2026, 2, 27)));
        Assert.True(Volljaehrigkeit.Ist(new DateOnly(2008, 2, 29), new DateOnly(2026, 2, 28)));
    }

    // ---------- Kind bearbeiten ----------

    [Fact]
    public async Task Kind_bearbeiten_folgt_der_Konfliktregel_und_erlaubt_ein_volljaehriges_Geburtsdatum()
    {
        var emma = await SendenAsync(new KindAnlegen(Katrin, "Emma", "Muster", Emma));

        var gespeichert = Assert.IsType<KindAenderung.Gespeichert>(await SendenAsync(new KindFeldAendern(Katrin, emma, KindFeld.Vorname, 1, "Emma", " Emilia ")));
        var konflikt = await SendenAsync(new KindFeldAendern(Katrin, emma, KindFeld.Vorname, 1, "Emma", "Ella"));
        var datum = Assert.IsType<KindAenderung.Gespeichert>(await SendenAsync(new KindFeldAendern(Katrin, emma, KindFeld.Geburtsdatum, 1, "2017-09-02", "2008-03-14")));

        Assert.Equal("Emilia", gespeichert.Person.Vorname);
        Assert.IsType<KindAenderung.Konflikt>(konflikt);
        Assert.Equal(new DateOnly(2008, 3, 14), datum.Person.Geburtsdatum);
        Assert.Equal("2008-03-14", datum.Person.Wert(KindFeld.Geburtsdatum));
    }

    [Fact]
    public async Task Kind_bearbeiten_nur_durch_ein_Elternteil_der_Familie_des_Kindes()
    {
        var emma = await SendenAsync(new KindAnlegen(Katrin, "Emma", null, Emma));
        await SendenAsync(new KindAnlegen(Daniel, "Mia", null, new DateOnly(2016, 5, 21)));

        Assert.IsType<KindAenderung.NichtGefunden>(await SendenAsync(new KindFeldAendern(Daniel, emma, KindFeld.Vorname, 1, "Emma", "X")));
        Assert.IsType<KindAenderung.NichtGefunden>(await SendenAsync(new KindFeldAendern(Thomas, emma, KindFeld.Vorname, 1, "Emma", "X")));
        Assert.IsType<KindAenderung.NichtGefunden>(await SendenAsync(new KindFeldAendern(Katrin, Katrin, KindFeld.Vorname, 1, null, "X")));
        Assert.Equal("Emma", await LesenAsync(db => db.Personen.Where(p => p.Id == emma).Select(p => p.Vorname).SingleAsync(Abbruch)));
    }

    [Theory]
    [InlineData(KindFeld.Vorname, "", "vorname", "Vorname fehlt.")]
    [InlineData(KindFeld.Vorname, null, "vorname", "Vorname fehlt.")]
    [InlineData(KindFeld.Geburtsdatum, "", "geburtsdatum", "Geburtsdatum fehlt.")]
    [InlineData(KindFeld.Geburtsdatum, "14.03.2008", "geburtsdatum", "Das Geburtsdatum ist ungültig.")]
    [InlineData(KindFeld.Geburtsdatum, "1899-01-01", "geburtsdatum", "Das Geburtsdatum ist ungültig.")]
    [InlineData(KindFeld.Geburtsdatum, "2026-07-02", "geburtsdatum", "Das Geburtsdatum liegt in der Zukunft.")]
    public async Task Kind_bearbeiten_prueft_das_Feld(KindFeld feld, string? wert, string schluessel, string meldung)
    {
        var emma = await SendenAsync(new KindAnlegen(Katrin, "Emma", null, Emma));

        var fehler = await Assert.ThrowsAsync<ValidierungsAusnahme>(() => SendenAsync(new KindFeldAendern(Katrin, emma, feld, 1, null, wert)));

        Assert.Equal([meldung], fehler.Fehler[schluessel]);
    }

    // ---------- Familien-Einladung ----------

    [Fact]
    public async Task Einladung_annehmen_macht_zum_zweiten_Elternteil_mit_gleichen_Rechten()
    {
        var emma = await SendenAsync(new KindAnlegen(Katrin, "Emma", null, Emma));
        var neu = Assert.IsType<ElternteilEinladung.Neu>(await SendenAsync(new ElternteilEinladenCommand(Katrin, " Thomas.Muster@Example.org ")));

        var einladung = await LesenAsync(db => db.FamilienEinladungen.SingleAsync(Abbruch));
        Assert.Equal((ThomasMail, _factory.Uhr.GetUtcNow() + TimeSpan.FromDays(7), Katrin), (einladung.Mail, einladung.GueltigBis, einladung.EingeladenVon));
        Assert.Equal(Zugangslink.TokenHash("familien-einladung", neu.Token), einladung.TokenHash);
        Assert.DoesNotContain(neu.Token, neu.ToString(), StringComparison.Ordinal);

        var angenommen = Assert.IsType<FamilienAnnahme.Angenommen>(await SendenAsync(new FamilienEinladungAnnehmenCommand(neu.Token, ThomasMail, Thomas)));

        Assert.Equal(einladung.Id, angenommen.EinladungId);
        Assert.Equal(2, await LesenAsync(db => db.Elternteile.CountAsync(e => e.FamilieId == einladung.FamilieId, Abbruch)));
        Assert.IsType<KindAenderung.Gespeichert>(await SendenAsync(new KindFeldAendern(Thomas, emma, KindFeld.Nachname, 1, null, "Muster")));
        Assert.IsType<ElternteilEinladung.Voll>(await SendenAsync(new ElternteilEinladenCommand(Thomas, "dritte@example.org")));
        Assert.Equal(1, await LesenAsync(db => db.FamilienEinladungen.CountAsync(Abbruch)));
    }

    [Fact]
    public async Task Einladung_ohne_Familie_legt_die_Familie_mit_der_Person_als_erstem_Elternteil_an()
    {
        Assert.IsType<ElternteilEinladung.Neu>(await SendenAsync(new ElternteilEinladenCommand(Katrin, ThomasMail)));

        Assert.Equal(Katrin, (await LesenAsync(db => db.Elternteile.SingleAsync(Abbruch))).PersonId);
    }

    public static TheoryData<string> UngueltigeEinladungen => ["abgelaufen", "zweites Annehmen", "verworfen", "andere Mail", "Übergabe-Token", "unbekannt", "Formfehler"];

    [Theory]
    [MemberData(nameof(UngueltigeEinladungen))]
    public async Task Ungueltige_Einladung_aendert_nichts(string fall)
    {
        var neu = Assert.IsType<ElternteilEinladung.Neu>(await SendenAsync(new ElternteilEinladenCommand(Katrin, ThomasMail)));
        var (token, mail, person, eltern) = (neu.Token, ThomasMail, Thomas, 1);
        switch (fall)
        {
            case "abgelaufen":
                _factory.Uhr.Advance(TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1));
                break;
            case "zweites Annehmen":
                Assert.IsType<FamilienAnnahme.Angenommen>(await SendenAsync(new FamilienEinladungAnnehmenCommand(token, mail, person)));
                eltern = 2;
                break;
            case "verworfen":
                Assert.IsType<ElternteilEinladung.Neu>(await SendenAsync(new ElternteilEinladenCommand(Katrin, "andere@example.org")));
                break;
            case "andere Mail":
                mail = "andere@example.org";
                break;
            case "Übergabe-Token":
                token = (await VolljaehrigUebergebenAsync()).Token;
                break;
            case "unbekannt":
                token = Zugangslink.NeuesToken();
                break;
            case "Formfehler":
                token = "kein-token";
                break;
        }

        Assert.IsType<FamilienAnnahme.Ungueltig>(await SendenAsync(new FamilienEinladungAnnehmenCommand(token, mail, person)));

        Assert.Equal(eltern, await LesenAsync(db => db.Elternteile.CountAsync(Abbruch)));
    }

    [Fact]
    public async Task Schon_Elternteil_derselben_Familie_nimmt_an_ohne_Aenderung()
    {
        var neu = Assert.IsType<ElternteilEinladung.Neu>(await SendenAsync(new ElternteilEinladenCommand(Katrin, ThomasMail)));

        Assert.IsType<FamilienAnnahme.Angenommen>(await SendenAsync(new FamilienEinladungAnnehmenCommand(neu.Token, ThomasMail, Katrin)));

        Assert.Equal(1, await LesenAsync(db => db.Elternteile.CountAsync(Abbruch)));
        Assert.Equal(FamilienEinladungStatus.Angenommen, await LesenAsync(db => db.FamilienEinladungen.Select(e => e.Status).SingleAsync(Abbruch)));
    }

    [Fact]
    public async Task Elternteil_einer_anderen_Familie_bekommt_einen_Konflikt_ohne_Aenderung()
    {
        await SendenAsync(new KindAnlegen(Daniel, "Mia", null, new DateOnly(2016, 5, 21)));
        var neu = Assert.IsType<ElternteilEinladung.Neu>(await SendenAsync(new ElternteilEinladenCommand(Katrin, ThomasMail)));

        Assert.IsType<FamilienAnnahme.Konflikt>(await SendenAsync(new FamilienEinladungAnnehmenCommand(neu.Token, ThomasMail, Daniel)));

        Assert.Equal(2, await LesenAsync(db => db.Elternteile.CountAsync(Abbruch)));
        Assert.Equal(FamilienEinladungStatus.Offen, await LesenAsync(db => db.FamilienEinladungen.Select(e => e.Status).SingleAsync(Abbruch)));
    }

    [Fact]
    public async Task Inzwischen_volle_Familie_ergibt_einen_Konflikt_ohne_Aenderung()
    {
        var neu = Assert.IsType<ElternteilEinladung.Neu>(await SendenAsync(new ElternteilEinladenCommand(Katrin, ThomasMail)));
        // Wie eine gleichzeitig angenommene zweite Einladung: Die Familie hat schon zwei Eltern.
        await AusfuehrenAsync($"INSERT INTO personen.familien_elternteil (\"FamilieId\", \"PersonId\") SELECT \"Id\", '{Daniel}' FROM personen.familie");

        Assert.IsType<FamilienAnnahme.Konflikt>(await SendenAsync(new FamilienEinladungAnnehmenCommand(neu.Token, ThomasMail, Thomas)));

        Assert.Equal(0, await LesenAsync(db => db.Elternteile.CountAsync(e => e.PersonId == Thomas, Abbruch)));
    }

    [Fact]
    public async Task Gleichzeitiges_Annehmen_derselben_Einladung_gelingt_genau_einmal()
    {
        var neu = Assert.IsType<ElternteilEinladung.Neu>(await SendenAsync(new ElternteilEinladenCommand(Katrin, ThomasMail)));

        var ergebnisse = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() => SendenAsync(new FamilienEinladungAnnehmenCommand(neu.Token, ThomasMail, Thomas)))));

        Assert.Single(ergebnisse, e => e is FamilienAnnahme.Angenommen);
        Assert.Equal(2, await LesenAsync(db => db.Elternteile.CountAsync(Abbruch)));
    }

    // ---------- Profil-Übergabe ----------

    [Fact]
    public async Task Uebergabe_nur_bei_volljaehrigem_Kind_und_nur_durch_ein_Elternteil_seiner_Familie()
    {
        var emma = await SendenAsync(new KindAnlegen(Katrin, "Emma", null, Emma));
        await SendenAsync(new KindAnlegen(Daniel, "Mia", null, new DateOnly(2016, 5, 21)));

        Assert.IsType<UebergabeAusloesung.NichtVolljaehrig>(await SendenAsync(new ProfilUebergebenCommand(Katrin, emma, PaulMail)));
        Assert.IsType<UebergabeAusloesung.NichtGefunden>(await SendenAsync(new ProfilUebergebenCommand(Daniel, emma, PaulMail)));
        Assert.IsType<UebergabeAusloesung.NichtGefunden>(await SendenAsync(new ProfilUebergebenCommand(Thomas, emma, PaulMail)));
        Assert.Equal(0, await LesenAsync(db => db.Uebergaben.CountAsync(Abbruch)));
    }

    [Fact]
    public async Task Neue_Uebergabe_verwirft_aeltere_offene_desselben_Kindes()
    {
        var erste = await VolljaehrigUebergebenAsync();
        var zweite = Assert.IsType<UebergabeAusloesung.Neu>(await SendenAsync(new ProfilUebergebenCommand(Katrin, erste.KindId, "paul@example.org")));

        var status = await LesenAsync(db => db.Uebergaben.OrderBy(u => u.AngelegtAm).ThenBy(u => u.Id).Select(u => new { u.Id, u.Status, u.GueltigBis }).ToListAsync(Abbruch));
        Assert.Equal([UebergabeStatus.Verworfen, UebergabeStatus.Offen], status.Select(s => s.Status).OrderBy(s => s == UebergabeStatus.Offen));
        Assert.Equal(_factory.Uhr.GetUtcNow() + TimeSpan.FromDays(14), status.Single(s => s.Id == zweite.UebergabeId).GueltigBis);
        Assert.Equal("Paul", zweite.Vorname);
        Assert.IsType<Uebernahme.Ungueltig>(await UebernehmenAsync(erste.Token, PaulMail));
    }

    [Fact]
    public async Task Annehmen_mit_leerem_Profil_loest_das_Kind_aus_der_Familie_loescht_die_leere_Person_und_legt_das_Ereignis_einmal_ab()
    {
        var neu = await VolljaehrigUebergebenAsync();
        await SendenAsync(new ProfilSicherstellen(Paul));

        var ergebnis = Assert.IsType<Uebernahme.Uebernommen>(await UebernehmenAsync(neu.Token, " Paul.Muster@example.org "));

        Assert.Equal((neu.UebergabeId, neu.KindId), (ergebnis.UebergabeId, ergebnis.KindId));
        var kind = await LesenAsync(db => db.Personen.SingleAsync(p => p.Id == neu.KindId, Abbruch));
        Assert.Null(kind.FamilieId);
        Assert.Equal("Paul", kind.Vorname);
        Assert.Equal(0, await LesenAsync(db => db.Personen.CountAsync(p => p.Id == Paul, Abbruch)));
        Assert.Equal(0, await LesenAsync(db => db.PersonenSchluessel.CountAsync(s => s.PersonId == Paul, Abbruch)));
        Assert.Equal(1, await LesenAsync(db => db.PersonenSchluessel.CountAsync(s => s.PersonId == neu.KindId, Abbruch)));
        Assert.Equal(UebergabeStatus.Angenommen, await LesenAsync(db => db.Uebergaben.Select(u => u.Status).SingleAsync(Abbruch)));
        Assert.Equal(1L, await WertAsync<long>("SELECT count(*) FROM personen.outbox WHERE \"Typ\" LIKE '%ProfilUebergabeAngenommen'"));
        Assert.Contains(PaulKonto.ToString(), await WertAsync<string>("SELECT \"Inhalt\"::text FROM personen.outbox"), StringComparison.Ordinal);
        Assert.IsType<UebergabeAusloesung.NichtGefunden>(await SendenAsync(new ProfilUebergebenCommand(Katrin, neu.KindId, PaulMail)));
        Assert.IsType<KindAenderung.NichtGefunden>(await SendenAsync(new KindFeldAendern(Katrin, neu.KindId, KindFeld.Vorname, 1, "Paul", "X")));

        Assert.IsType<Uebernahme.Ungueltig>(await UebernehmenAsync(neu.Token, PaulMail));
        Assert.Equal(1L, await WertAsync<long>("SELECT count(*) FROM personen.outbox"));
    }

    [Fact]
    public async Task Annehmen_ohne_eigene_Person_gelingt_ebenfalls()
    {
        var neu = await VolljaehrigUebergebenAsync();

        Assert.IsType<Uebernahme.Uebernommen>(await UebernehmenAsync(neu.Token, PaulMail));
    }

    public static TheoryData<string> EigeneProfile => ["Vorname", "Nachname", "Freizeit-Rolle", "Freigabe", "Elternteil"];

    [Theory]
    [MemberData(nameof(EigeneProfile))]
    public async Task Eigenes_Profil_mit_Inhalt_ergibt_einen_Konflikt_ohne_Aenderung(string fall)
    {
        var neu = await VolljaehrigUebergebenAsync();
        var person = await SendenAsync(new ProfilSicherstellen(Paul));
        switch (fall)
        {
            case "Vorname":
                await SendenAsync(new ProfilFeldAendern(Paul, ProfilFeld.Vorname, person.Version, null, "Paul"));
                break;
            case "Nachname":
                await SendenAsync(new ProfilFeldAendern(Paul, ProfilFeld.Nachname, person.Version, null, "Muster"));
                break;
            case "Freizeit-Rolle":
                await RolleZustellenAsync(Paul);
                break;
            case "Freigabe":
                // Eine eigene Anmeldung (z. B. über den Link für Mitarbeitende) hinterlässt eine Freigabe, auch ohne Namen.
                await FreigabeZustellenAsync(Paul);
                break;
            case "Elternteil":
                await SendenAsync(new ElternteilEinladenCommand(Paul, "irgendwer@example.org"));
                break;
        }

        Assert.IsType<Uebernahme.EigenesProfil>(await UebernehmenAsync(neu.Token, PaulMail));

        Assert.Equal(1, await LesenAsync(db => db.Personen.CountAsync(p => p.Id == Paul, Abbruch)));
        Assert.NotNull(await LesenAsync(db => db.Personen.Where(p => p.Id == neu.KindId).Select(p => p.FamilieId).SingleAsync(Abbruch)));
        Assert.Equal(UebergabeStatus.Offen, await LesenAsync(db => db.Uebergaben.Select(u => u.Status).SingleAsync(Abbruch)));
        Assert.Equal(0L, await WertAsync<long>("SELECT count(*) FROM personen.outbox"));
    }

    public static TheoryData<string> UngueltigeUebergaben => ["abgelaufen", "andere Mail", "Einladungs-Token", "unbekannt", "Formfehler"];

    [Theory]
    [MemberData(nameof(UngueltigeUebergaben))]
    public async Task Ungueltige_Uebergabe_aendert_nichts(string fall)
    {
        var neu = await VolljaehrigUebergebenAsync();
        var (token, mail) = (neu.Token, PaulMail);
        switch (fall)
        {
            case "abgelaufen":
                _factory.Uhr.Advance(TimeSpan.FromDays(14) + TimeSpan.FromMinutes(1));
                break;
            case "andere Mail":
                mail = ThomasMail;
                break;
            case "Einladungs-Token":
                token = Assert.IsType<ElternteilEinladung.Neu>(await SendenAsync(new ElternteilEinladenCommand(Katrin, PaulMail))).Token;
                break;
            case "unbekannt":
                token = Zugangslink.NeuesToken();
                break;
            case "Formfehler":
                token = null;
                break;
        }

        Assert.IsType<Uebernahme.Ungueltig>(await UebernehmenAsync(token, mail));

        Assert.NotNull(await LesenAsync(db => db.Personen.Where(p => p.Id == neu.KindId).Select(p => p.FamilieId).SingleAsync(Abbruch)));
        Assert.Equal(0L, await WertAsync<long>("SELECT count(*) FROM personen.outbox"));
    }

    [Fact]
    public async Task Ist_das_Kind_nach_einer_Aenderung_des_Geburtsdatums_nicht_mehr_volljaehrig_gilt_der_Link_nicht()
    {
        var neu = await VolljaehrigUebergebenAsync();
        Assert.IsType<KindAenderung.Gespeichert>(await SendenAsync(new KindFeldAendern(Katrin, neu.KindId, KindFeld.Geburtsdatum, 1, "2008-03-14", "2015-03-14")));
        await SendenAsync(new ProfilSicherstellen(Paul));

        Assert.IsType<Uebernahme.Ungueltig>(await UebernehmenAsync(neu.Token, PaulMail));

        Assert.NotNull(await LesenAsync(db => db.Personen.Where(p => p.Id == neu.KindId).Select(p => p.FamilieId).SingleAsync(Abbruch)));
        Assert.Equal(1, await LesenAsync(db => db.Personen.CountAsync(p => p.Id == Paul, Abbruch)));
        Assert.Equal(UebergabeStatus.Offen, await LesenAsync(db => db.Uebergaben.Select(u => u.Status).SingleAsync(Abbruch)));
        Assert.Equal(0L, await WertAsync<long>("SELECT count(*) FROM personen.outbox"));
        Assert.Equal(1L, await WertAsync<long>("SELECT count(*) FROM betrieb.sicherheitsprotokoll WHERE \"Art\" = 3"));
    }

    [Fact]
    public async Task Gleichzeitiges_Annehmen_derselben_Uebergabe_gelingt_genau_einmal()
    {
        var neu = await VolljaehrigUebergebenAsync();

        var ergebnisse = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => UebernehmenAsync(neu.Token, PaulMail))));

        Assert.Single(ergebnisse, e => e is Uebernahme.Uebernommen);
        Assert.Equal(1L, await WertAsync<long>("SELECT count(*) FROM personen.outbox"));
    }

    // ---------- Hilfen ----------

    /// <summary>Katrin legt Paul an, setzt sein Geburtsdatum auf volljährig (2008-03-14) und übergibt an <see cref="PaulMail"/>.</summary>
    private async Task<UebergabeAusloesung.Neu> VolljaehrigUebergebenAsync()
    {
        var paul = await SendenAsync(new KindAnlegen(Katrin, "Paul", "Muster", new DateOnly(2015, 3, 14)));
        Assert.IsType<KindAenderung.Gespeichert>(await SendenAsync(new KindFeldAendern(Katrin, paul, KindFeld.Geburtsdatum, 1, "2015-03-14", "2008-03-14")));
        return Assert.IsType<UebergabeAusloesung.Neu>(await SendenAsync(new ProfilUebergebenCommand(Katrin, paul, PaulMail)));
    }

    private Task<Uebernahme> UebernehmenAsync(string? token, string mail) =>
        SendenAsync(new ProfilUebernehmenCommand(token, mail, PaulKonto, Paul));

    private async Task RolleZustellenAsync(Guid person)
    {
        var organisation = Guid.CreateVersion7();
        var freizeit = Guid.CreateVersion7();
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(
            FreizeitRolleUebernehmen.Aus(new Umschlag(Guid.CreateVersion7(), DateTimeOffset.UtcNow, organisation, freizeit, 1), new FreizeitRolleGeaendert(freizeit, person, FreizeitRolleArt.Leitung)),
            Abbruch);
    }

    private async Task FreigabeZustellenAsync(Guid person)
    {
        var organisation = Guid.CreateVersion7();
        var freizeit = Guid.CreateVersion7();
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(
            FreigabeAnlegen.Aus(
                new Umschlag(Guid.CreateVersion7(), DateTimeOffset.UtcNow, organisation, freizeit, 1),
                new AnmeldungEingegangen(Guid.CreateVersion7(), freizeit, person, AnmeldungArt.Mitarbeitende, DateTimeOffset.UtcNow.AddDays(30))),
            Abbruch);
    }

    private async Task<T> SendenAsync<T>(ICommand<T> command)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, Abbruch);
    }

    private async Task<T> LesenAsync<T>(Func<PersonenLeseDbContext, Task<T>> abfrage)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await abfrage(scope.ServiceProvider.GetRequiredService<PersonenLeseDbContext>());
    }

    private async Task AusfuehrenAsync(string sql)
    {
        await using var db = new NpgsqlConnection(_verbindung);
        await db.OpenAsync(Abbruch);
        await using var befehl = new NpgsqlCommand(sql, db);
        await befehl.ExecuteNonQueryAsync(Abbruch);
    }

    private async Task<T> WertAsync<T>(string sql)
    {
        await using var db = new NpgsqlConnection(_verbindung);
        await db.OpenAsync(Abbruch);
        await using var befehl = new NpgsqlCommand(sql, db);
        return (T)(await befehl.ExecuteScalarAsync(Abbruch))!;
    }
}
