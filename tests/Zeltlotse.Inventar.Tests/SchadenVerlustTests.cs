using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Inventar;
using Zeltlotse.Inventar.Daten;
using Zeltlotse.Inventar.Schaeden;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.Sicherheit.Contracts.Events;
using Zeltlotse.Testing;

namespace Zeltlotse.Inventar.Tests;

/// <summary>
/// Story 3-4, Schäden, Verluste und Anschaffungswünsche (FR-32, FR-38, AD-6, AD-19): Eintragen mit Trimmen, die Entnahme aus dem Vorfall-Protokoll
/// als „Erste-Hilfe-Kasten“ ohne Text, Feldänderung mit Konfliktregel, die Formprüfung, der Empfänger von <see cref="ErsteHilfeEntnommen"/> (nichts
/// doppelt), der Lösch-Lauf und der Mandantenfilter.
/// </summary>
public sealed class SchadenVerlustTests(PostgresContainer postgres)
{
    private static readonly DateTimeOffset Jetzt = new(2027, 8, 5, 10, 30, 0, TimeSpan.Zero);

    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    private static SchadenVerlust Neu(SchadenArt art = SchadenArt.Verlust, string bezeichnung = "Taschenlampe") =>
        SchadenVerlust.Eintragen(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), art, bezeichnung, Jetzt);

    // ---------- Entität ----------

    [Fact]
    public void Eintragen_trimmt_die_Bezeichnung_und_hat_keinen_Vorfall_Eintrag()
    {
        var eintrag = Neu(SchadenArt.Schaden, "  Großzelt: Stange verbogen ");

        Assert.Equal((SchadenArt.Schaden, "Großzelt: Stange verbogen", Jetzt, (Guid?)null), (eintrag.Art, eintrag.Bezeichnung, eintrag.ErfasstAm, eintrag.VorfallEintragId));
    }

    [Fact]
    public void AusEntnahme_heisst_Erste_Hilfe_Kasten_mit_der_Kennung_des_Vorfall_Eintrags()
    {
        var (organisation, freizeit, vorfall) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        var eintrag = SchadenVerlust.AusEntnahme(Guid.CreateVersion7(), organisation, freizeit, vorfall, Jetzt);

        Assert.Equal(
            (organisation, freizeit, SchadenArt.Entnahme, "Erste-Hilfe-Kasten", Jetzt, (Guid?)vorfall),
            (eintrag.OrganisationId, eintrag.FreizeitId, eintrag.Art, eintrag.Bezeichnung, eintrag.ErfasstAm, eintrag.VorfallEintragId));
    }

    [Fact]
    public void FeldAendern_setzt_getrimmt_wenn_der_alte_Wert_stimmt_sonst_Konflikt()
    {
        var eintrag = Neu();

        Assert.True(eintrag.FeldAendern(SchadenFeld.Bezeichnung, " Taschenlampe ", " Stirnlampe "));
        Assert.Equal("Stirnlampe", eintrag.Wert(SchadenFeld.Bezeichnung));
        Assert.False(eintrag.FeldAendern(SchadenFeld.Bezeichnung, "Taschenlampe", "Laterne"));
        Assert.Equal("Stirnlampe", eintrag.Bezeichnung);
        Assert.False(eintrag.FeldAendern(SchadenFeld.Bezeichnung, null, "Laterne"));
    }

    // ---------- Formprüfung ----------

    [Theory]
    [InlineData(null, "Taschenlampe", "art")]
    [InlineData(9, "Taschenlampe", "art")]
    [InlineData(2, null, "bezeichnung")]
    [InlineData(2, "   ", "bezeichnung")]
    public void Eintragen_verlangt_eine_bekannte_Art_und_eine_Bezeichnung(int? art, string? bezeichnung, string feld)
    {
        var ergebnis = new SchadenEintragenValidator().Validate(new SchadenEintragenCommand(Guid.CreateVersion7(), (SchadenArt?)art, bezeichnung));

        Assert.Equal(feld, Assert.Single(ergebnis.Errors).PropertyName);
    }

    [Fact]
    public void Validatoren_erlauben_alle_drei_Arten_und_hoechstens_120_Zeichen_nach_dem_Trimmen()
    {
        foreach (var art in Enum.GetValues<SchadenArt>())
        {
            Assert.True(new SchadenEintragenValidator().Validate(new SchadenEintragenCommand(Guid.CreateVersion7(), art, $" {new string('a', 120)} ")).IsValid);
        }

        var zuLang = new SchadenEintragenValidator().Validate(new SchadenEintragenCommand(Guid.CreateVersion7(), SchadenArt.Schaden, new string('a', 121)));
        Assert.Equal(("bezeichnung", "Höchstens 120 Zeichen."), (Assert.Single(zuLang.Errors).PropertyName, zuLang.Errors[0].ErrorMessage));

        static FluentValidation.Results.ValidationResult Aendern(SchadenFeld feld, string? wert) =>
            new SchadenFeldAendernValidator().Validate(new SchadenFeldAendernCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), feld, 0, null, wert));

        Assert.True(Aendern(SchadenFeld.Bezeichnung, "Stirnlampe").IsValid);
        Assert.Equal("bezeichnung", Assert.Single(Aendern(SchadenFeld.Bezeichnung, " ").Errors).PropertyName);
        Assert.Equal("bezeichnung", Assert.Single(Aendern(SchadenFeld.Bezeichnung, new string('a', 121)).Errors).PropertyName);
        Assert.Equal("feld", Assert.Single(Aendern((SchadenFeld)99, "x").Errors).PropertyName);
    }

    // ---------- Datenbank ----------

    [Fact]
    public async Task Empfaenger_legt_den_Eintrag_Erste_Hilfe_Kasten_an_und_bei_derselben_Kennung_nichts_doppelt()
    {
        await using var factory = await FactoryAsync();
        var (organisation, freizeit, vorfall) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var ereignis = new ErsteHilfeEntnommen(vorfall, freizeit, Jetzt);

        await EmpfangenAsync(factory, organisation, ereignis);
        await EmpfangenAsync(factory, organisation, ereignis);

        var eintrag = Assert.Single(await LesenAsync(factory, organisation, db => db.Schaeden.ToListAsync(Abbruch)));
        Assert.Equal(
            (organisation, freizeit, SchadenArt.Entnahme, "Erste-Hilfe-Kasten", Jetzt, (Guid?)vorfall),
            (eintrag.OrganisationId, eintrag.FreizeitId, eintrag.Art, eintrag.Bezeichnung, eintrag.ErfasstAm, eintrag.VorfallEintragId));
        Assert.NotEqual(0u, eintrag.Version);
    }

    [Fact]
    public async Task Loeschlauf_loescht_die_Eintraege_der_abgelaufenen_Freizeiten_andere_bleiben()
    {
        await using var factory = await FactoryAsync();
        var (orgA, orgB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var (abgelaufen, laeuft) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        await AnlegenAsync(factory, orgA, abgelaufen, "Taschenlampe");
        await AnlegenAsync(factory, orgB, abgelaufen, "Kompass");
        var bleibt = await AnlegenAsync(factory, orgA, laeuft, "Kochtopf");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var lauf = scope.ServiceProvider.GetServices<ILoeschlauf>().Single(l => l.GetType().Name == "InventarLoeschlauf");
            await lauf.LaufAsync(new LoeschAuftrag(Jetzt, [abgelaufen], []), Abbruch);
        }

        Assert.Equal([bleibt], await LesenAsync(factory, orgA, db => db.Schaeden.Select(s => s.Id).ToListAsync(Abbruch)));
        Assert.Empty(await LesenAsync(factory, orgB, db => db.Schaeden.ToListAsync(Abbruch)));
    }

    [Fact]
    public async Task Mandantenfilter_der_Lese_Kontext_liefert_je_Mandant_nur_die_eigenen_Eintraege_die_Sperre_ebenso()
    {
        await using var factory = await FactoryAsync();
        var (orgA, orgB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var freizeit = Guid.CreateVersion7();
        var inA = await AnlegenAsync(factory, orgA, freizeit, "Taschenlampe");
        var inB = await AnlegenAsync(factory, orgB, Guid.CreateVersion7(), "Kompass");

        Assert.Equal([inA], await LesenAsync(factory, orgA, db => db.Schaeden.Select(s => s.Id).ToListAsync(Abbruch)));
        Assert.Equal([inB], await LesenAsync(factory, orgB, db => db.Schaeden.Select(s => s.Id).ToListAsync(Abbruch)));
        Assert.Empty(await LesenAsync(factory, null, db => db.Schaeden.ToListAsync(Abbruch)));

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(orgB);
        var schreibend = scope.ServiceProvider.GetRequiredService<InventarDbContext>();
        Assert.Equal([inB], await schreibend.Schaeden.Select(s => s.Id).ToListAsync(Abbruch));
        Assert.Null(await schreibend.SchadenSperrenAsync(freizeit, inA, Abbruch));
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(orgA);
        Assert.Null(await schreibend.SchadenSperrenAsync(Guid.CreateVersion7(), inA, Abbruch));
        Assert.NotNull(await schreibend.SchadenSperrenAsync(freizeit, inA, Abbruch));
    }

    // ---------- Helfer ----------

    private async Task<ZeltlotseApiFactory> FactoryAsync()
    {
        var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync());
        _ = factory.CreateClient();
        return factory;
    }

    private static async Task<Guid> AnlegenAsync(ZeltlotseApiFactory factory, Guid organisation, Guid freizeit, string bezeichnung)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        var db = scope.ServiceProvider.GetRequiredService<InventarDbContext>();
        var eintrag = SchadenVerlust.Eintragen(Guid.CreateVersion7(), organisation, freizeit, SchadenArt.Verlust, bezeichnung, Jetzt);
        db.Schaeden.Add(eintrag);
        await db.SaveChangesAsync(Abbruch);
        return eintrag.Id;
    }

    /// <summary>Ruft den Handler des Empfängers direkt auf (ohne Inbox) und speichert, wie die Pipeline es täte.</summary>
    private static async Task EmpfangenAsync(ZeltlotseApiFactory factory, Guid organisation, ErsteHilfeEntnommen ereignis)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        var db = scope.ServiceProvider.GetRequiredService<InventarDbContext>();
        var umschlag = new Umschlag(Guid.CreateVersion7(), Jetzt, organisation, ereignis.VorfallEintragId, 1);
        var handler = new ErsteHilfeEntnahmeEintragenHandler(db, scope.ServiceProvider.GetRequiredService<IMandantenKontext>(), TimeProvider.System);
        await handler.Handle(ErsteHilfeEntnahmeEintragen.Aus(umschlag, ereignis), Abbruch);
        await db.SaveChangesAsync(Abbruch);
    }

    private static async Task<T> LesenAsync<T>(ZeltlotseApiFactory factory, Guid? organisation, Func<InventarLeseDbContext, Task<T>> abfrage)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        return await abfrage(scope.ServiceProvider.GetRequiredService<InventarLeseDbContext>());
    }
}
