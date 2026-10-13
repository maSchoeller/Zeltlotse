using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Freizeiten.Daten;
using Zeltlotse.Freizeiten.Dokumente;
using Zeltlotse.Freizeiten.Verwaltung;
using Zeltlotse.Organisationen.Contracts.Dokumente;
using Zeltlotse.SharedKernel.Dateien;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.Testing;

namespace Zeltlotse.Freizeiten.Tests;

/// <summary>
/// Story 2-7: das Dokument der Freizeit (FR-43, AD-6, AD-15, AD-19): Titel aus dem Dateinamen, Feldänderung mit Konfliktregel und
/// gesperrtem Titel bei Einbindungen, Einbindung ohne Titel und ohne Datei, Datei unter <c>freizeiten/&lt;id&gt;</c> und der
/// Mandantenfilter. Die Org-Dokumente liefert hier eine feste Leseschnittstelle.
/// </summary>
public sealed class DokumentTests(PostgresContainer postgres) : IAsyncLifetime
{
    private static readonly Guid OrgA = Guid.CreateVersion7();
    private static readonly Guid OrgB = Guid.CreateVersion7();
    private static readonly Guid Andrea = Guid.CreateVersion7();
    private static readonly Guid Hausordnung = Guid.CreateVersion7();
    private static readonly byte[] Pdf = [.. "%PDF-1.7\nPackliste\n"u8, 1, 2, 3];
    private static readonly DateTimeOffset Jetzt = new(2026, 10, 13, 8, 0, 0, TimeSpan.Zero);

    private readonly DateiSpeicherImSpeicher _speicher = new();
    private ZeltlotseApiFactory _factory = null!;

    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync());
        _factory.ZusatzDienste.Add(services =>
        {
            services.RemoveAll<IDateiSpeicher>();
            services.AddSingleton<IDateiSpeicher>(_speicher);
            services.RemoveAll<IOrgDokumenteLesen>();
            services.AddScoped<IOrgDokumenteLesen>(sp => new FesteOrgDokumente(sp.GetRequiredService<IMandantenKontext>()));
        });
        _ = _factory.CreateClient();
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    // ---------- Entität ----------

    [Theory]
    [InlineData("C:\\Ablage\\Packliste 2027.pdf", "Packliste 2027")]
    [InlineData("infobrief.v2.pdf", "infobrief.v2")]
    [InlineData("  Notfall\u0007nummern .png", "Notfallnummern")]
    [InlineData(" .pdf", "Dokument")]
    [InlineData(null, "Dokument")]
    public void Titel_kommt_aus_dem_Dateinamen_ohne_Pfad_Endung_und_Steuerzeichen(string? dateiname, string titel) =>
        Assert.Equal(titel, DokumentDateien.TitelAusDateiname(dateiname));

    [Fact]
    public void Ein_langer_Dateiname_wird_auf_200_Zeichen_gekuerzt() =>
        Assert.Equal(new string('a', 200), DokumentDateien.TitelAusDateiname(new string('a', 250) + ".pdf"));

    [Fact]
    public void Feldaenderung_mit_Konfliktregel_je_Feld()
    {
        var dokument = Dokument.Hochladen(Guid.CreateVersion7(), OrgA, Guid.CreateVersion7(), "Packliste", DokumentSichtbarkeit.Team, DateiArt.Pdf, 10, Jetzt);

        Assert.Equal(DokumentFeldErgebnis.Gespeichert, dokument.FeldAendern(DokumentFeld.Titel, "Packliste", " Packliste 2027 "));
        Assert.Equal("Packliste 2027", dokument.Wert(DokumentFeld.Titel));
        Assert.Equal(DokumentFeldErgebnis.Konflikt, dokument.FeldAendern(DokumentFeld.Titel, "Packliste", "Anders"));
        Assert.Equal(DokumentFeldErgebnis.Gespeichert, dokument.FeldAendern(DokumentFeld.Sichtbarkeit, "Team", "Oeffentlich"));
        Assert.Equal((DokumentSichtbarkeit.Oeffentlich, "Oeffentlich"), (dokument.Sichtbarkeit, dokument.Wert(DokumentFeld.Sichtbarkeit)));
        Assert.Equal(DokumentFeldErgebnis.Konflikt, dokument.FeldAendern(DokumentFeld.Sichtbarkeit, "Team", "ElternUndTeilnehmende"));
        Assert.Equal("Packliste 2027", dokument.Titel);
    }

    [Fact]
    public void Der_alte_Wert_der_Sichtbarkeit_zaehlt_ohne_Gross_und_Kleinschreibung()
    {
        var dokument = Dokument.Hochladen(Guid.CreateVersion7(), OrgA, Guid.CreateVersion7(), "Packliste", DokumentSichtbarkeit.Team, DateiArt.Pdf, 10, Jetzt);

        Assert.Equal(DokumentFeldErgebnis.Gespeichert, dokument.FeldAendern(DokumentFeld.Sichtbarkeit, "team", "oeffentlich"));
        Assert.Equal(DokumentSichtbarkeit.Oeffentlich, dokument.Sichtbarkeit);
        Assert.Equal(DokumentFeldErgebnis.Konflikt, dokument.FeldAendern(DokumentFeld.Sichtbarkeit, null, "Team"));
    }

    [Fact]
    public void Ein_geaenderter_Titel_verliert_Steuerzeichen_und_Zeilenumbrueche()
    {
        var dokument = Dokument.Hochladen(Guid.CreateVersion7(), OrgA, Guid.CreateVersion7(), "Packliste", DokumentSichtbarkeit.Team, DateiArt.Pdf, 10, Jetzt);

        Assert.Equal(DokumentFeldErgebnis.Gespeichert, dokument.FeldAendern(DokumentFeld.Titel, "Packliste", " Pack\nliste\u0007 2027\r\n"));

        Assert.Equal("Packliste 2027", dokument.Titel);
        Assert.Equal("", DokumentDateien.OhneSteuerzeichen("\n\t"));
    }

    [Fact]
    public async Task Gleichzeitiges_Einbinden_desselben_Org_Dokuments_gelingt_genau_einmal()
    {
        var freizeit = await FreizeitAsync(OrgA);

        var ergebnisse = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() => SendenAsync(new DokumentEinbindenCommand(freizeit, Hausordnung), OrgA))));

        Assert.Single(ergebnisse, e => e is DokumentAenderung.Eingebunden);
        Assert.Equal(3, ergebnisse.Count(e => e is DokumentAenderung.SchonEingebunden));
        Assert.Single(await LesenAsync(db => db.Dokumente.ToListAsync(Abbruch), OrgA));
    }

    [Fact]
    public void Eine_Einbindung_hat_keinen_Titel_keine_Art_und_Sichtbarkeit_Team_den_Titel_pflegt_die_Organisation()
    {
        var einbindung = Dokument.Einbinden(Guid.CreateVersion7(), OrgA, Guid.CreateVersion7(), Hausordnung, Jetzt);

        Assert.Equal((null, null, null, DokumentSichtbarkeit.Team, true), (einbindung.Titel, einbindung.Dateiart, einbindung.Groesse, einbindung.Sichtbarkeit, einbindung.AusOrganisation));
        Assert.Equal(DokumentFeldErgebnis.NurOrganisation, einbindung.FeldAendern(DokumentFeld.Titel, null, "Neu"));
        Assert.Equal(DokumentFeldErgebnis.Gespeichert, einbindung.FeldAendern(DokumentFeld.Sichtbarkeit, "Team", "ElternUndTeilnehmende"));
    }

    // ---------- Commands ----------

    [Fact]
    public async Task Hochladen_speichert_Titel_aus_dem_Dateinamen_und_die_Datei_unverschluesselt_unter_freizeiten()
    {
        var freizeit = await FreizeitAsync(OrgA);

        var ergebnis = await SendenAsync(new DokumentHochladenCommand(freizeit, "Packliste.pdf", DokumentSichtbarkeit.ElternUndTeilnehmende, Pdf, DateiArt.Pdf), OrgA);

        var dokument = Assert.IsType<DokumentAenderung.Gespeichert>(ergebnis).Dokument;
        Assert.Equal(("Packliste", DateiArt.Pdf, (long?)Pdf.Length), (dokument.Titel, dokument.Dateiart, dokument.Groesse));
        Assert.Equal(Pdf, _speicher.Objekte[$"freizeiten/{dokument.Id}"].Inhalt);
        Assert.Equal("application/pdf", _speicher.Objekte[$"freizeiten/{dokument.Id}"].ContentType);
        // Weder Dateiname noch Bytes im Log (AD-22).
        var text = new DokumentHochladenCommand(freizeit, "Packliste.pdf", DokumentSichtbarkeit.Team, Pdf, DateiArt.Pdf).ToString();
        Assert.DoesNotContain("Packliste", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Byte", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Einbinden_legt_eine_Zeile_ohne_Titel_an_zweites_Einbinden_und_unbekannte_Dokumente_nicht()
    {
        var freizeit = await FreizeitAsync(OrgA);

        var eingebunden = await SendenAsync(new DokumentEinbindenCommand(freizeit, Hausordnung), OrgA);
        var zweites = await SendenAsync(new DokumentEinbindenCommand(freizeit, Hausordnung), OrgA);
        var unbekannt = await SendenAsync(new DokumentEinbindenCommand(freizeit, Guid.CreateVersion7()), OrgA);

        var dokument = Assert.IsType<DokumentAenderung.Eingebunden>(eingebunden).Dokument;
        Assert.Equal((Hausordnung, null), (dokument.OrgDokumentId, dokument.Titel));
        Assert.IsType<DokumentAenderung.SchonEingebunden>(zweites);
        Assert.IsType<DokumentAenderung.NichtGefunden>(unbekannt);
        Assert.Empty(_speicher.Objekte);
        Assert.Single(await LesenAsync(db => db.Dokumente.ToListAsync(Abbruch), OrgA));
    }

    [Fact]
    public async Task Mandantenfilter_Dokumente_nur_im_eigenen_Kontext()
    {
        var freizeit = await FreizeitAsync(OrgA);
        await SendenAsync(new DokumentHochladenCommand(freizeit, "Packliste.pdf", DokumentSichtbarkeit.Team, Pdf, DateiArt.Pdf), OrgA);

        Assert.Single(await LesenAsync(db => db.Dokumente.ToListAsync(Abbruch), OrgA));
        foreach (var kontext in new Guid?[] { OrgB, null })
        {
            Assert.Empty(await LesenAsync(db => db.Dokumente.ToListAsync(Abbruch), kontext));
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(OrgB);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<FreizeitenDbContext>().Dokumente.ToListAsync(Abbruch));
    }

    // ---------- Hilfen ----------

    private Task<Guid> FreizeitAsync(Guid organisation) =>
        SendenAsync(new FreizeitAnlegenCommand(organisation, "Zeltlager", new DateOnly(2027, 8, 2), new DateOnly(2027, 8, 13), Andrea), organisation);

    private async Task<T> SendenAsync<T>(ICommand<T> command, Guid? organisationId = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisationId);
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, Abbruch);
    }

    private async Task<T> LesenAsync<T>(Func<FreizeitenLeseDbContext, Task<T>> abfrage, Guid? organisationId = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisationId);
        return await abfrage(scope.ServiceProvider.GetRequiredService<FreizeitenLeseDbContext>());
    }

    /// <summary>Org-Dokumente der Organisation A: nur die Hausordnung, und nur im Kontext von A.</summary>
    private sealed class FesteOrgDokumente(IMandantenKontext mandant) : IOrgDokumenteLesen
    {
        private IReadOnlyList<OrgDokumentInfo> Alle =>
            mandant.OrganisationId == OrgA ? [new OrgDokumentInfo(Hausordnung, "Hausordnung", DateiArt.Pdf, 10, Jetzt)] : [];

        public Task<IReadOnlyList<OrgDokumentInfo>> ListeAsync(CancellationToken abbruch = default) => Task.FromResult(Alle);

        public Task<IReadOnlyDictionary<Guid, OrgDokumentInfo>> InfosAsync(IReadOnlyCollection<Guid> ids, CancellationToken abbruch = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, OrgDokumentInfo>>(Alle.Where(d => ids.Contains(d.Id)).ToDictionary(d => d.Id));

        public Task<OrgDokumentDatei?> DateiAsync(Guid id, CancellationToken abbruch = default) =>
            Task.FromResult(Alle.Any(d => d.Id == id) ? new OrgDokumentDatei(Pdf, "application/pdf", DateiArt.Pdf) : null);
    }
}
