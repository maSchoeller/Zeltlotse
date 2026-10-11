using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.Testing;
using Zeltlotse.Testmodul.Notizen;

namespace Zeltlotse.SharedKernel.Tests;

/// <summary>Matrix-Zeilen „Command gültig“, „Command ungültig“, „Handler wirft“ und der Generator-Test (AD-27).</summary>
public sealed class PipelineTests(PostgresContainer postgres)
{
    private static readonly Guid OrgA = Guid.Parse("0199a000-0000-7000-8000-00000000000a");

    [Fact]
    public async Task Generator_findet_interne_Handler_ueber_InternalsVisibleTo()
    {
        await using var umgebung = await TestUmgebung.ErstellenAsync(postgres);
        await using var scope = umgebung.Dienste.CreateAsyncScope();

        var handler = scope.ServiceProvider.GetService<ICommandHandler<NotizAnlegen, Guid>>();

        Assert.NotNull(handler);
        Assert.False(typeof(NotizAnlegen).IsPublic);
        Assert.False(handler.GetType().IsPublic);
    }

    [Fact]
    public async Task Gueltiger_Command_speichert_Aenderung_und_Outbox_Zeile_mit_Folgenummer()
    {
        await using var umgebung = await TestUmgebung.ErstellenAsync(postgres);

        var id = await umgebung.SendenAsync(new NotizAnlegen(OrgA, "Zeltplatz prüfen"), OrgA);
        await umgebung.SendenAsync(new NotizAendern(id, "Zeltplatz geprüft"), OrgA);

        var notiz = await umgebung.RohLesenAsync(db => db.Notizen.IgnoreQueryFilters().SingleAsync(n => n.Id == id));
        Assert.Equal("Zeltplatz geprüft", notiz.Text);
        Assert.Equal(2, notiz.Folgenummer);

        var outbox = await umgebung.RohLesenAsync(db => db.Outbox.OrderBy(o => o.Folgenummer).ToListAsync());
        Assert.Collection(
            outbox,
            o =>
            {
                Assert.Equal(id, o.DatensatzId);
                Assert.Equal(1, o.Folgenummer);
                Assert.Equal(OrgA, o.OrganisationId);
                Assert.Equal(umgebung.Uhr.GetUtcNow(), o.Zeitpunkt);
                Assert.Equal(7, o.Id.Version);
                Assert.EndsWith("NotizAngelegt", o.Typ);
            },
            o =>
            {
                Assert.Equal(id, o.DatensatzId);
                Assert.Equal(2, o.Folgenummer);
                Assert.EndsWith("NotizGeaendert", o.Typ);
            });
    }

    [Fact]
    public async Task Gleichzeitige_Aenderung_desselben_Aggregats_scheitert_an_der_Folgenummer()
    {
        await using var umgebung = await TestUmgebung.ErstellenAsync(postgres);
        var id = await umgebung.SendenAsync(new NotizAnlegen(OrgA, "Plan"), OrgA);

        // Scope A lädt die Notiz (Folgenummer 1) und hält sie fest ...
        await using var scopeA = umgebung.Dienste.CreateAsyncScope();
        scopeA.ServiceProvider.GetRequiredService<Mandanten.MandantenKontext>().Setzen(OrgA);
        var dbA = scopeA.ServiceProvider.GetRequiredService<Testmodul.Daten.TestmodulDbContext>();
        _ = await dbA.Notizen.SingleAsync(n => n.Id == id, TestContext.Current.CancellationToken);

        // ... währenddessen ändert Scope B sie (Folgenummer 2).
        await umgebung.SendenAsync(new NotizAendern(id, "Plan B"), OrgA);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(async () =>
            await scopeA.ServiceProvider.GetRequiredService<ISender>()
                .Send(new NotizAendern(id, "Plan A"), TestContext.Current.CancellationToken));

        var folgenummern = await umgebung.RohLesenAsync(db => db.Outbox.Select(o => o.Folgenummer).OrderBy(f => f).ToListAsync());
        Assert.Equal([1L, 2L], folgenummern);
    }

    [Fact]
    public async Task Ungueltiger_Command_laesst_Handler_nicht_laufen_und_meldet_Fehler_je_JSON_Feld()
    {
        await using var umgebung = await TestUmgebung.ErstellenAsync(postgres);

        var ausnahme = await Assert.ThrowsAsync<ValidierungsAusnahme>(
            () => umgebung.SendenAsync(new NotizAnlegen(OrgA, ""), OrgA));

        Assert.Contains("text", ausnahme.Fehler.Keys);
        Assert.DoesNotContain("Text", ausnahme.Fehler.Keys);
        Assert.Equal(0, await umgebung.RohLesenAsync(db => db.Notizen.IgnoreQueryFilters().CountAsync()));
        Assert.Equal(0, await umgebung.RohLesenAsync(db => db.Outbox.CountAsync()));
    }

    [Fact]
    public async Task Werfender_Handler_speichert_weder_Aenderung_noch_Outbox_Zeile()
    {
        await using var umgebung = await TestUmgebung.ErstellenAsync(postgres);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => umgebung.SendenAsync(new NotizAnlegenUndScheitern(OrgA, "geht schief"), OrgA));

        Assert.Equal(0, await umgebung.RohLesenAsync(db => db.Notizen.IgnoreQueryFilters().CountAsync()));
        Assert.Equal(0, await umgebung.RohLesenAsync(db => db.Outbox.CountAsync()));
        Assert.Equal(0, await umgebung.RohLesenAsync(db => db.Zustellungen.CountAsync()));
    }

    [Theory]
    [InlineData("Text", "text")]
    [InlineData("Adresse.Strasse", "adresse.strasse")]
    [InlineData("Posten[0].Name", "posten[0].name")]
    public void Fehlerschluessel_sind_JSON_Namen_auch_fuer_verschachtelte_Felder(string eigenschaft, string erwartet) =>
        Assert.Equal(erwartet, Pipeline.ValidierungsStufe<NotizAnlegen, Guid>.JsonName(eigenschaft));
}
