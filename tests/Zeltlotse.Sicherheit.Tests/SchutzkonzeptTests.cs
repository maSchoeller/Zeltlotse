using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Sicherheit;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.Sicherheit.Daten;
using Zeltlotse.Sicherheit.Schutzkonzept;
using Zeltlotse.Testing;
using Konzept = Zeltlotse.Sicherheit.Schutzkonzept.Schutzkonzept;

namespace Zeltlotse.Sicherheit.Tests;

/// <summary>
/// Story 2-9, Schutzkonzept (FR-30, AD-3, AD-6, AD-19): Feldänderung mit Konfliktregel gegen das wirksame Dokument, die Formprüfung beider
/// Commands und der Mandantenfilter des Schutzkonzepts und der Bestätigungen.
/// </summary>
public sealed class SchutzkonzeptTests(PostgresContainer postgres)
{
    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    private static Konzept Neu() => Konzept.Anlegen(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

    // ---------- Entität ----------

    [Fact]
    public void FeldAendern_setzt_das_Dokument_wenn_der_alte_Wert_stimmt()
    {
        var konzept = Neu();
        var d1 = Guid.CreateVersion7();

        Assert.True(konzept.FeldAendern(SchutzkonzeptFeld.DokumentId, null, d1, dokumentVorhanden: false));

        Assert.Equal(d1, konzept.DokumentId);
        Assert.Equal(d1, konzept.Wirksam(dokumentVorhanden: true));
    }

    [Fact]
    public void FeldAendern_mit_abweichendem_alten_Wert_ist_ein_Konflikt_und_aendert_nichts()
    {
        var konzept = Neu();
        var (d1, d2) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        Assert.True(konzept.FeldAendern(SchutzkonzeptFeld.DokumentId, null, d1, dokumentVorhanden: false));

        Assert.False(konzept.FeldAendern(SchutzkonzeptFeld.DokumentId, null, d2, dokumentVorhanden: true));
        Assert.False(konzept.FeldAendern(SchutzkonzeptFeld.DokumentId, d2, d2, dokumentVorhanden: true));

        Assert.Equal(d1, konzept.DokumentId);
        Assert.True(konzept.FeldAendern(SchutzkonzeptFeld.DokumentId, d1, null, dokumentVorhanden: true));
        Assert.Null(konzept.DokumentId);
    }

    [Fact]
    public void Ein_verschwundenes_Dokument_gilt_als_kein_Schutzkonzept_auch_fuer_den_alten_Wert()
    {
        var konzept = Neu();
        var (d1, d2) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        konzept.FeldAendern(SchutzkonzeptFeld.DokumentId, null, d1, dokumentVorhanden: false);

        Assert.Null(konzept.Wirksam(dokumentVorhanden: false));
        Assert.False(konzept.FeldAendern(SchutzkonzeptFeld.DokumentId, d1, d2, dokumentVorhanden: false));
        Assert.True(konzept.FeldAendern(SchutzkonzeptFeld.DokumentId, null, d2, dokumentVorhanden: false));

        Assert.Equal(d2, konzept.DokumentId);
    }

    // ---------- Formprüfung ----------

    [Fact]
    public void Validator_der_Feldaenderung_lehnt_ein_unbekanntes_Feld_ab()
    {
        var validator = new SchutzkonzeptFeldAendernValidator();

        Assert.True(validator.Validate(new SchutzkonzeptFeldAendernCommand(Guid.CreateVersion7(), SchutzkonzeptFeld.DokumentId, 0, null, Guid.CreateVersion7())).IsValid);
        Assert.True(validator.Validate(new SchutzkonzeptFeldAendernCommand(Guid.CreateVersion7(), SchutzkonzeptFeld.DokumentId, 0, null, null)).IsValid);
        var fehler = Assert.Single(validator.Validate(new SchutzkonzeptFeldAendernCommand(Guid.CreateVersion7(), (SchutzkonzeptFeld)9, 0, null, null)).Errors);
        Assert.Equal("feld", fehler.PropertyName);
    }

    [Fact]
    public void Validator_der_Bestaetigung_verlangt_Dokument_und_Person()
    {
        var validator = new SchutzkonzeptGelesenValidator();

        Assert.True(validator.Validate(new SchutzkonzeptGelesenCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7())).IsValid);
        var ohneDokument = Assert.Single(validator.Validate(new SchutzkonzeptGelesenCommand(Guid.CreateVersion7(), Guid.Empty, Guid.CreateVersion7())).Errors);
        Assert.Equal(SchutzkonzeptFelder.DokumentId, ohneDokument.PropertyName);
        Assert.Equal("personId", Assert.Single(validator.Validate(new SchutzkonzeptGelesenCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.Empty)).Errors).PropertyName);
    }

    [Fact]
    public void Die_Commands_geben_keine_Kennungen_ausser_der_Freizeit_aus()
    {
        var person = Guid.CreateVersion7();

        Assert.DoesNotContain(person.ToString(), new SchutzkonzeptGelesenCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), person).ToString(), StringComparison.Ordinal);
    }

    // ---------- Mandantenfilter ----------

    [Fact]
    public async Task Mandantenfilter_der_Lese_Kontext_liefert_je_Mandant_nur_eigene_Schutzkonzepte_und_Bestaetigungen()
    {
        await using var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync());
        _ = factory.CreateClient();
        var (orgA, orgB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var (x, z) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        await SpeichernAsync(factory, orgA, db =>
        {
            db.Schutzkonzepte.Add(Konzept.Anlegen(Guid.CreateVersion7(), orgA, x));
            db.SchutzkonzeptBestaetigungen.Add(SchutzkonzeptBestaetigung.Anlegen(Guid.CreateVersion7(), orgA, x, Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow));
        });
        await SpeichernAsync(factory, orgB, db =>
        {
            db.Schutzkonzepte.Add(Konzept.Anlegen(Guid.CreateVersion7(), orgB, z));
            db.SchutzkonzeptBestaetigungen.Add(SchutzkonzeptBestaetigung.Anlegen(Guid.CreateVersion7(), orgB, z, Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow));
        });

        foreach (var (organisation, erwartet) in new (Guid?, Guid[])[] { (orgA, [x]), (orgB, [z]), (null, []) })
        {
            var (konzepte, bestaetigungen) = await FreizeitenAsync(factory, organisation);
            Assert.Equal(erwartet, konzepte);
            Assert.Equal(erwartet, bestaetigungen);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(orgB);
        var schreiben = scope.ServiceProvider.GetRequiredService<SicherheitDbContext>();
        Assert.Equal([z], await schreiben.Schutzkonzepte.Select(k => k.FreizeitId).ToListAsync(Abbruch));
        Assert.Equal([z], await schreiben.SchutzkonzeptBestaetigungen.Select(k => k.FreizeitId).ToListAsync(Abbruch));
    }

    private static async Task SpeichernAsync(ZeltlotseApiFactory factory, Guid organisation, Action<SicherheitDbContext> hinzufuegen)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        var db = scope.ServiceProvider.GetRequiredService<SicherheitDbContext>();
        hinzufuegen(db);
        await db.SaveChangesAsync(Abbruch);
    }

    private static async Task<(List<Guid> Konzepte, List<Guid> Bestaetigungen)> FreizeitenAsync(ZeltlotseApiFactory factory, Guid? organisation)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        var db = scope.ServiceProvider.GetRequiredService<SicherheitLeseDbContext>();
        return (
            await db.Schutzkonzepte.Select(k => k.FreizeitId).ToListAsync(Abbruch),
            await db.SchutzkonzeptBestaetigungen.Select(b => b.FreizeitId).ToListAsync(Abbruch));
    }
}
