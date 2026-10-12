using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Personen.Contracts.Events;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.ArchitectureTests.Negativ;

// Negativ-Fixtures für AD-16. Werden nie ausgeführt, nur von den Architekturtests ausgewertet.

/// <summary>Ein Modul-DbContext mit einer Entität mit Personenbezug.</summary>
internal sealed class NegativTeilnahmeDbContext(DbContextOptions<NegativTeilnahmeDbContext> optionen, IMandantenKontext kontext)
    : ModulDbContext(optionen, kontext)
{
    public DbSet<NegativTeilnahme> Teilnahmen => Set<NegativTeilnahme>();

    protected override string Schema => "negativ";

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder)
    {
    }
}

internal sealed class NegativTeilnahme
{
    public Guid Id { get; set; }

    public Guid PersonId { get; set; }
}

/// <summary>Die Auskunft, die das Modul erst regelkonform macht.</summary>
internal sealed class NegativAuskunft : IDatenauskunft
{
    public Task<IReadOnlyList<AuskunftAbschnitt>> AuskunftAsync(Guid personId, CancellationToken abbruch) =>
        Task.FromResult<IReadOnlyList<AuskunftAbschnitt>>([]);
}

/// <summary>Der Empfänger, den das Modul außerdem braucht.</summary>
internal sealed record NegativPersonLoeschen(Umschlag Umschlag, Guid PersonId)
    : ICommand, IModulCommand<NegativTeilnahmeDbContext>, IEreignisEmpfaenger<NegativPersonLoeschen, PersonGeloescht>
{
    public static NegativPersonLoeschen Aus(Umschlag umschlag, PersonGeloescht ereignis) => new(umschlag, ereignis.PersonId);
}
