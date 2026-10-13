using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zeltlotse.Organisationen.Contracts.Dokumente;
using Zeltlotse.Organisationen.Daten;
using Zeltlotse.Organisationen.Dokumente;
using Zeltlotse.SharedKernel.Dateien;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.Testing;

namespace Zeltlotse.Organisationen.Tests;

/// <summary>
/// Story 2-7: Org-Dokumente (FR-43, AD-6, AD-15): Titel aus dem Dateinamen, Datei unverschlüsselt unter
/// <c>organisationen/&lt;id&gt;</c>, Entfernen mit Datei, die Leseschnittstelle für Freizeiten und der Mandantenfilter.
/// </summary>
public sealed class OrgDokumentTests(PostgresContainer postgres) : IAsyncLifetime
{
    private static readonly Guid OrgA = Guid.CreateVersion7();
    private static readonly Guid OrgB = Guid.CreateVersion7();
    private static readonly byte[] Pdf = [.. "%PDF-1.7\nHausordnung\n"u8, 1, 2, 3];

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
        });
        _ = _factory.CreateClient();
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Hochladen_nimmt_den_Titel_aus_dem_Dateinamen_und_legt_die_Datei_unverschluesselt_ab()
    {
        var dokument = await SendenAsync(new OrgDokumentHochladenCommand(OrgA, "C:\\Ablage\\Hausordnung Zeltplatz.pdf", Pdf, DateiArt.Pdf), OrgA);
        var ohneName = await SendenAsync(new OrgDokumentHochladenCommand(OrgA, " .pdf", Pdf, DateiArt.Pdf), OrgA);

        Assert.Equal(("Hausordnung Zeltplatz", DateiArt.Pdf, (long)Pdf.Length, OrgA), (dokument.Titel, dokument.Dateiart, dokument.Groesse, dokument.OrganisationId));
        Assert.Equal("Dokument", ohneName.Titel);
        Assert.Equal(Pdf, _speicher.Objekte[$"organisationen/{dokument.Id}"].Inhalt);
        Assert.DoesNotContain("Hausordnung", new OrgDokumentHochladenCommand(OrgA, "Hausordnung.pdf", Pdf, DateiArt.Pdf).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Entfernen_loescht_Zeile_und_Datei_ein_Dokument_einer_anderen_Organisation_bleibt()
    {
        var dokument = await SendenAsync(new OrgDokumentHochladenCommand(OrgA, "Hausordnung.pdf", Pdf, DateiArt.Pdf), OrgA);

        Assert.False(await SendenAsync(new OrgDokumentEntfernenCommand(OrgB, dokument.Id), OrgB));
        Assert.True(await SendenAsync(new OrgDokumentEntfernenCommand(OrgA, dokument.Id), OrgA));
        Assert.False(await SendenAsync(new OrgDokumentEntfernenCommand(OrgA, dokument.Id), OrgA));

        Assert.Empty(_speicher.Objekte);
        Assert.Empty(await LesenAsync(db => db.Dokumente.ToListAsync(Abbruch), OrgA));
    }

    [Fact]
    public async Task Leseschnittstelle_liefert_Liste_Infos_und_Datei_nur_der_Organisation_des_Kontexts()
    {
        var hausordnung = await SendenAsync(new OrgDokumentHochladenCommand(OrgA, "Hausordnung.pdf", Pdf, DateiArt.Pdf), OrgA);
        var leitfaden = await SendenAsync(new OrgDokumentHochladenCommand(OrgA, "leitfaden.pdf", Pdf, DateiArt.Pdf), OrgA);
        var vonB = await SendenAsync(new OrgDokumentHochladenCommand(OrgB, "B.pdf", Pdf, DateiArt.Pdf), OrgB);

        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(OrgA);
        var lesen = scope.ServiceProvider.GetRequiredService<IOrgDokumenteLesen>();

        Assert.Equal(["Hausordnung", "leitfaden"], (await lesen.ListeAsync(Abbruch)).Select(d => d.Titel));
        var infos = await lesen.InfosAsync([hausordnung.Id, vonB.Id, Guid.CreateVersion7()], Abbruch);
        Assert.Equal([hausordnung.Id], infos.Keys);
        Assert.Equal((DateiArt.Pdf, (long)Pdf.Length), (infos[hausordnung.Id].Dateiart, infos[hausordnung.Id].Groesse));
        var datei = await lesen.DateiAsync(leitfaden.Id, Abbruch);
        Assert.Equal(Pdf, datei!.Inhalt);
        Assert.Equal(("application/pdf", DateiArt.Pdf), (datei.ContentType, datei.Dateiart));
        Assert.Null(await lesen.DateiAsync(vonB.Id, Abbruch));
    }

    [Fact]
    public async Task Mandantenfilter_Org_Dokumente_nur_im_eigenen_Kontext()
    {
        await SendenAsync(new OrgDokumentHochladenCommand(OrgA, "Hausordnung.pdf", Pdf, DateiArt.Pdf), OrgA);

        Assert.Single(await LesenAsync(db => db.Dokumente.ToListAsync(Abbruch), OrgA));
        Assert.Empty(await LesenAsync(db => db.Dokumente.ToListAsync(Abbruch), OrgB));
        Assert.Empty(await LesenAsync(db => db.Dokumente.ToListAsync(Abbruch)));
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(OrgB);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<OrganisationenDbContext>().Dokumente.ToListAsync(Abbruch));
    }

    private async Task<T> SendenAsync<T>(ICommand<T> command, Guid? organisationId = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisationId);
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, Abbruch);
    }

    private async Task<T> LesenAsync<T>(Func<OrganisationenLeseDbContext, Task<T>> abfrage, Guid? organisationId = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisationId);
        return await abfrage(scope.ServiceProvider.GetRequiredService<OrganisationenLeseDbContext>());
    }
}
