using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Personen.Nachweise;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.Testing;
using EreignisArt = Zeltlotse.Personen.Contracts.Events.NachweisArt;
using OrgArt = Zeltlotse.Api.Contracts.Organisationen.NachweisArt;
using PersonenArt = Zeltlotse.Api.Contracts.Personen.NachweisArt;

namespace Zeltlotse.Personen.Tests;

/// <summary>
/// Nachweise am Profil (FR-8, AD-14, AD-15): anlegen mit Feldregeln, je Art höchstens einer, Ausstellungsdatum nach AD-19,
/// Datei nur verschlüsselt im Dateispeicher; eine neue Datei oder ein geändertes Datum löscht den Ablehnungshinweis.
/// </summary>
public sealed class NachweisTests(PostgresContainer postgres) : NachweisTestBasis(postgres)
{
    [Fact]
    public async Task Anlegen_speichert_Art_und_Datum_ohne_Datei_und_die_Datei_nur_verschluesselt()
    {
        var id = Assert.IsType<NachweisAnlage.Angelegt>(await SendenAsync(new NachweisAnlegen(Jonas, EreignisArt.Fuehrungszeugnis, new DateOnly(2025, 11, 3)))).NachweisId;

        var nachweis = await LesenAsync(db => db.Nachweise.SingleAsync(n => n.Id == id, Abbruch));
        Assert.Equal((Jonas, EreignisArt.Fuehrungszeugnis, new DateOnly(2025, 11, 3), (DateTimeOffset?)null), (nachweis.PersonId, nachweis.Art, nachweis.Ausstellungsdatum, nachweis.HochgeladenAm));

        var hochgeladen = await SendenAsync(new NachweisDateiHochladen(Jonas, id, Pdf));

        Assert.Equal(new NachweisDateiErgebnis.Hochgeladen(Factory.Uhr.GetUtcNow()), hochgeladen);
        Assert.Equal(Factory.Uhr.GetUtcNow(), (await LesenAsync(db => db.Nachweise.SingleAsync(n => n.Id == id, Abbruch))).HochgeladenAm);
        var roh = Speicher.Objekte[$"personen/{id}"];
        Assert.NotEqual(Pdf, roh.Inhalt);
        Assert.Equal("application/octet-stream", roh.ContentType);
        Assert.Equal(Pdf, await DateiLesenAsync(id));
    }

    public static TheoryData<EreignisArt?, string?, string, string> Ungueltig => new()
    {
        { null, "2025-11-03", "art", "Art fehlt." },
        { (EreignisArt)9, "2025-11-03", "art", "Art fehlt." },
        { EreignisArt.Juleica, null, "ausstellungsdatum", "Ausstellungsdatum fehlt." },
        { EreignisArt.Juleica, "2026-07-02", "ausstellungsdatum", "Das Ausstellungsdatum liegt in der Zukunft." },
        { EreignisArt.Juleica, "1899-12-31", "ausstellungsdatum", "Das Ausstellungsdatum ist ungültig." },
    };

    [Theory]
    [MemberData(nameof(Ungueltig))]
    public async Task Anlegen_prueft_jedes_Feld(EreignisArt? art, string? datum, string feld, string meldung)
    {
        var tag = datum is null ? (DateOnly?)null : DateOnly.Parse(datum, System.Globalization.CultureInfo.InvariantCulture);

        var fehler = await Assert.ThrowsAsync<ValidierungsAusnahme>(() => SendenAsync(new NachweisAnlegen(Jonas, art, tag)));

        Assert.Equal([feld], fehler.Fehler.Keys);
        Assert.Equal([meldung], fehler.Fehler[feld]);
        Assert.Equal(0, await LesenAsync(db => db.Nachweise.CountAsync(Abbruch)));
    }

    [Fact]
    public async Task Heute_in_Berlin_ist_kein_Datum_in_der_Zukunft()
    {
        // 01.07.2026 08:00 UTC ist in Berlin der 01.07.2026; auch kurz vor Mitternacht Berliner Zeit zählt noch der Tag.
        Factory.Uhr.SetUtcNow(new DateTimeOffset(2026, 7, 1, 21, 59, 0, TimeSpan.Zero));

        Assert.IsType<NachweisAnlage.Angelegt>(await SendenAsync(new NachweisAnlegen(Jonas, EreignisArt.Juleica, new DateOnly(2026, 7, 1))));
        await Assert.ThrowsAsync<ValidierungsAusnahme>(() => SendenAsync(new NachweisAnlegen(Jonas, EreignisArt.ErsteHilfe, new DateOnly(2026, 7, 2))));
        Factory.Uhr.SetUtcNow(new DateTimeOffset(2026, 7, 1, 22, 0, 0, TimeSpan.Zero));
        Assert.IsType<NachweisAnlage.Angelegt>(await SendenAsync(new NachweisAnlegen(Jonas, EreignisArt.ErsteHilfe, new DateOnly(2026, 7, 2))));
    }

    [Fact]
    public async Task Je_Art_hoechstens_ein_Nachweis_je_Person()
    {
        await SendenAsync(new NachweisAnlegen(Jonas, EreignisArt.Fuehrungszeugnis, new DateOnly(2025, 11, 3)));

        Assert.IsType<NachweisAnlage.Vorhanden>(await SendenAsync(new NachweisAnlegen(Jonas, EreignisArt.Fuehrungszeugnis, new DateOnly(2025, 12, 1))));
        Assert.IsType<NachweisAnlage.Angelegt>(await SendenAsync(new NachweisAnlegen(Lea, EreignisArt.Fuehrungszeugnis, new DateOnly(2025, 12, 1))));
        Assert.Equal(1, await LesenAsync(db => db.Nachweise.CountAsync(n => n.PersonId == Jonas, Abbruch)));
    }

    [Fact]
    public async Task Datum_aendern_folgt_der_Konfliktregel_und_nur_am_eigenen_Nachweis()
    {
        var id = await AnlegenAsync(EreignisArt.Juleica, new DateOnly(2024, 5, 20));

        var gespeichert = Assert.IsType<NachweisAenderung.Gespeichert>(
            await SendenAsync(new NachweisFeldAendern(Jonas, id, NachweisFeld.Ausstellungsdatum, 1, "2024-05-20", "2024-05-21")));
        Assert.IsType<NachweisAenderung.Konflikt>(
            await SendenAsync(new NachweisFeldAendern(Jonas, id, NachweisFeld.Ausstellungsdatum, 1, "2024-05-20", "2024-06-01")));
        Assert.IsType<NachweisAenderung.NichtGefunden>(
            await SendenAsync(new NachweisFeldAendern(Lea, id, NachweisFeld.Ausstellungsdatum, 1, "2024-05-21", "2024-06-01")));

        Assert.Equal(new DateOnly(2024, 5, 21), gespeichert.Nachweis.Ausstellungsdatum);
        Assert.Equal("2024-05-21", gespeichert.Nachweis.Wert(NachweisFeld.Ausstellungsdatum));
    }

    [Theory]
    [InlineData(null, "Ausstellungsdatum fehlt.")]
    [InlineData("03.11.2025", "Das Ausstellungsdatum ist ungültig.")]
    [InlineData("1899-01-01", "Das Ausstellungsdatum ist ungültig.")]
    [InlineData("2026-07-02", "Das Ausstellungsdatum liegt in der Zukunft.")]
    public async Task Datum_aendern_prueft_das_Feld(string? wert, string meldung)
    {
        var id = await AnlegenAsync(EreignisArt.Juleica, new DateOnly(2024, 5, 20));

        var fehler = await Assert.ThrowsAsync<ValidierungsAusnahme>(
            () => SendenAsync(new NachweisFeldAendern(Jonas, id, NachweisFeld.Ausstellungsdatum, 1, "2024-05-20", wert)));

        Assert.Equal([meldung], fehler.Fehler["ausstellungsdatum"]);
    }

    [Fact]
    public async Task Datei_nur_am_eigenen_Nachweis_und_eine_neue_Datei_ersetzt_die_alte()
    {
        var id = await AnlegenAsync(EreignisArt.ErsteHilfe, new DateOnly(2025, 4, 15));
        await SendenAsync(new NachweisDateiHochladen(Jonas, id, Pdf));

        Assert.IsType<NachweisDateiErgebnis.NichtGefunden>(await SendenAsync(new NachweisDateiHochladen(Lea, id, Png)));
        Factory.Uhr.Advance(TimeSpan.FromHours(1));
        Assert.Equal(new NachweisDateiErgebnis.Hochgeladen(Factory.Uhr.GetUtcNow()), await SendenAsync(new NachweisDateiHochladen(Jonas, id, Png)));

        Assert.Single(Speicher.Objekte);
        Assert.Equal(Png, await DateiLesenAsync(id));
        Assert.IsType<NachweisDateiErgebnis.NichtGefunden>(await SendenAsync(new NachweisDateiHochladen(Jonas, Guid.CreateVersion7(), Png)));
    }

    [Fact]
    public async Task Waehrend_der_Pruefung_sind_Datum_und_Datei_gesperrt_nach_Entscheidung_und_nach_Ablauf_wieder_frei()
    {
        var org = Guid.CreateVersion7();
        var freizeit = Guid.CreateVersion7();
        var fz = await AnlegenAsync(EreignisArt.Fuehrungszeugnis, new DateOnly(2025, 11, 3));
        var eh = await AnlegenAsync(EreignisArt.ErsteHilfe, new DateOnly(2025, 4, 15));
        await SendenAsync(new NachweisDateiHochladen(Jonas, fz, Pdf));
        await SendenAsync(new NachweisDateiHochladen(Jonas, eh, Png));
        await EinsichtHilfe.RolleAsync(this, org, freizeit, Jonas);
        await EinsichtHilfe.RolleAsync(this, org, freizeit, Miriam);
        var einsicht = Assert.IsType<EinsichtAnfrageErgebnis.Vorhanden>(
            await SendenAsync(new EinsichtAnfragenCommand(org, freizeit, Miriam, Jonas), org)).EinsichtId;
        Assert.IsType<EinsichtFreigabe.Freigegeben>(await SendenAsync(new EinsichtFreigeben(Jonas, einsicht)));

        Assert.IsType<NachweisAenderung.InPruefung>(
            await SendenAsync(new NachweisFeldAendern(Jonas, fz, NachweisFeld.Ausstellungsdatum, 1, "2025-11-03", "2025-11-04")));
        Assert.IsType<NachweisDateiErgebnis.InPruefung>(await SendenAsync(new NachweisDateiHochladen(Jonas, fz, Png)));
        Assert.Equal(Pdf, await DateiLesenAsync(fz));

        Assert.IsType<PruefungUebernahme.Gebunden>(await SendenAsync(new EinsichtPruefungUebernehmen(freizeit, Miriam, einsicht), org));
        Assert.True(await SendenAsync(new NachweisEntscheiden(Miriam, einsicht, eh, Bestaetigt: true, null), org));
        Assert.IsType<NachweisAenderung.Gespeichert>(
            await SendenAsync(new NachweisFeldAendern(Jonas, eh, NachweisFeld.Ausstellungsdatum, 1, "2025-04-15", "2025-04-16")));
        Assert.IsType<NachweisDateiErgebnis.InPruefung>(await SendenAsync(new NachweisDateiHochladen(Jonas, fz, Png)));

        Factory.Uhr.Advance(TimeSpan.FromDays(14) + TimeSpan.FromMinutes(1));
        Assert.IsType<NachweisDateiErgebnis.Hochgeladen>(await SendenAsync(new NachweisDateiHochladen(Jonas, fz, Png)));
        Assert.IsType<NachweisAenderung.Gespeichert>(
            await SendenAsync(new NachweisFeldAendern(Jonas, fz, NachweisFeld.Ausstellungsdatum, 1, "2025-11-03", "2025-11-04")));
    }

    [Fact]
    public async Task Neue_Datei_und_geaendertes_Datum_loeschen_den_Ablehnungshinweis()
    {
        var (id, nachweis) = await AbgelehntAsync("Datum passt nicht");
        Assert.NotNull(nachweis.Ablehnungshinweis);

        await SendenAsync(new NachweisDateiHochladen(Jonas, id, Png));
        Assert.Null((await LesenAsync(db => db.Nachweise.SingleAsync(n => n.Id == id, Abbruch))).Ablehnungshinweis);

        var (id2, _) = await AbgelehntAsync("Unleserlich", EreignisArt.Juleica);
        await SendenAsync(new NachweisFeldAendern(Jonas, id2, NachweisFeld.Ausstellungsdatum, 1, "2024-05-20", "2024-05-22"));
        Assert.Null((await LesenAsync(db => db.Nachweise.SingleAsync(n => n.Id == id2, Abbruch))).Ablehnungshinweis);
    }

    [Fact]
    public void Die_drei_Nachweisarten_haben_dieselben_Namen_und_Werte()
    {
        static IEnumerable<(string, int)> Werte<T>()
            where T : struct, Enum =>
            Enum.GetValues<T>().Select(w => (w.ToString(), Convert.ToInt32(w, System.Globalization.CultureInfo.InvariantCulture)));

        Assert.Equal(Werte<EreignisArt>(), Werte<PersonenArt>());
        Assert.Equal(Werte<EreignisArt>(), Werte<OrgArt>());
        Assert.Equal([("Fuehrungszeugnis", 1), ("Juleica", 2), ("ErsteHilfe", 3), ("Praeventionsschulung", 4)], Werte<EreignisArt>());
    }

    private async Task<Guid> AnlegenAsync(EreignisArt art, DateOnly datum) =>
        Assert.IsType<NachweisAnlage.Angelegt>(await SendenAsync(new NachweisAnlegen(Jonas, art, datum))).NachweisId;

    /// <summary>Jonas legt den Nachweis mit Datei an, Miriam (Leitung) fragt an, Jonas gibt frei, Miriam lehnt ab.</summary>
    private async Task<(Guid Id, Nachweis Nachweis)> AbgelehntAsync(string hinweis, EreignisArt art = EreignisArt.ErsteHilfe)
    {
        var org = Guid.CreateVersion7();
        var freizeit = Guid.CreateVersion7();
        var id = await AnlegenAsync(art, art == EreignisArt.Juleica ? new DateOnly(2024, 5, 20) : new DateOnly(2025, 4, 15));
        await SendenAsync(new NachweisDateiHochladen(Jonas, id, Pdf));
        await EinsichtHilfe.RolleAsync(this, org, freizeit, Jonas);
        await EinsichtHilfe.RolleAsync(this, org, freizeit, Miriam);
        var einsicht = Assert.IsType<EinsichtAnfrageErgebnis.Vorhanden>(
            await SendenAsync(new EinsichtAnfragenCommand(org, freizeit, Miriam, Jonas), org)).EinsichtId;
        Assert.IsType<EinsichtFreigabe.Freigegeben>(await SendenAsync(new EinsichtFreigeben(Jonas, einsicht)));
        Assert.IsType<PruefungUebernahme.Gebunden>(await SendenAsync(new EinsichtPruefungUebernehmen(freizeit, Miriam, einsicht), org));
        Assert.True(await SendenAsync(new NachweisEntscheiden(Miriam, einsicht, id, Bestaetigt: false, hinweis), org));
        return (id, await LesenAsync(db => db.Nachweise.SingleAsync(n => n.Id == id, Abbruch)));
    }
}
