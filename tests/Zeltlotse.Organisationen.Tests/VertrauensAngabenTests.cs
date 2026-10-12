using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Organisationen;
using Zeltlotse.Organisationen.Contracts.Vertrauensseite;
using Zeltlotse.Organisationen.Datenschutz;
using Zeltlotse.Organisationen.Nachweise;
using Zeltlotse.Organisationen.Verwaltung;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.Testing;
using EreignisArt = Zeltlotse.Personen.Contracts.Events.NachweisArt;
using StandArt = Zeltlotse.Organisationen.Contracts.Nachweise.NachweisArt;

namespace Zeltlotse.Organisationen.Tests;

/// <summary>
/// Story 2-1: die Angaben der Organisation für die Vertrauensseite (FR-66) über <see cref="IVertrauensAngabenLesen"/> –
/// Name, Löschfristen und Pflicht-Nachweise aus den gespeicherten Einstellungen, nur im Mandanten-Kontext (AD-6).
/// </summary>
public sealed class VertrauensAngabenTests(PostgresContainer postgres) : IAsyncLifetime
{
    private static readonly Guid Gerd = Guid.CreateVersion7();

    private ZeltlotseApiFactory _factory = null!;

    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync());
        _ = _factory.CreateClient();
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Liefert_Name_Vorgabefristen_und_Pflicht_Nachweise_und_folgt_den_Aenderungen()
    {
        var org = (await SendenAsync(new OrganisationAnlegenCommand("CVJM Musterstadt", "andrea@example.org", Gerd))).OrganisationId;

        var vorher = await LesenAsync(org);
        Assert.Equal(("CVJM Musterstadt", 12, 12), (vorher!.Name, vorher.FreizeitDatenMonate, vorher.NachweisBestaetigungMonate));
        Assert.Equal([StandArt.Fuehrungszeugnis], vorher.PflichtArten);

        await SendenAsync(new LoeschfristFeldAendern(org, LoeschfristFeld.FreizeitDatenMonate, 1, "12", "24"), org);
        await SendenAsync(new RichtlinieFeldAendern(org, EreignisArt.ErsteHilfe, RichtlinieFeld.Pflicht, 1, "false", "true"), org);

        var nachher = await LesenAsync(org);
        Assert.Equal(("CVJM Musterstadt", 24, 12), (nachher!.Name, nachher.FreizeitDatenMonate, nachher.NachweisBestaetigungMonate));
        Assert.Equal([StandArt.Fuehrungszeugnis, StandArt.ErsteHilfe], nachher.PflichtArten);
    }

    [Fact]
    public async Task Ohne_Mandanten_Kontext_oder_fuer_eine_unbekannte_Organisation_nichts()
    {
        await SendenAsync(new OrganisationAnlegenCommand("CVJM Musterstadt", "andrea@example.org", Gerd));

        Assert.Null(await LesenAsync(null));
        Assert.Null(await LesenAsync(Guid.CreateVersion7()));
    }

    private async Task<VertrauensAngaben?> LesenAsync(Guid? organisationId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisationId);
        return await scope.ServiceProvider.GetRequiredService<IVertrauensAngabenLesen>().LesenAsync(Abbruch);
    }

    private async Task<T> SendenAsync<T>(ICommand<T> command, Guid? organisationId = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisationId);
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, Abbruch);
    }
}
