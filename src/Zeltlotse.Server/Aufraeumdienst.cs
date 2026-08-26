using Microsoft.EntityFrameworkCore;
using Zeltlotse.Core.Freizeiten;
using Zeltlotse.Core.Persistenz;

namespace Zeltlotse.Server;

/// <summary>
/// Leert den Papierkorb. Was länger als 30 Tage als gelöscht markiert ist,
/// verschwindet endgültig — Freizeiten wie ganze Organisationen. Ohne diesen
/// Dienst wäre die Frist ein Versprechen ohne Deckung.
/// </summary>
public sealed class Aufraeumdienst(IServiceProvider dienste, ILogger<Aufraeumdienst> protokoll)
    : BackgroundService
{
    private static readonly TimeSpan Takt = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken abbruch)
    {
        using var uhr = new PeriodicTimer(Takt);

        do
        {
            try
            {
                await AufraeumenAsync(abbruch);
            }
            catch (Exception fehler) when (fehler is not OperationCanceledException)
            {
                // Ein gescheiterter Lauf darf den Dienst nicht beenden — sonst
                // bliebe der Papierkorb bis zum nächsten Neustart ungeleert.
                protokoll.LogError(fehler, "Aufräumen fehlgeschlagen.");
            }
        }
        while (await uhr.WaitForNextTickAsync(abbruch));
    }

    /// <summary>Öffentlich, damit der Abnahmetest die Frist wirklich prüfen kann.</summary>
    public async Task AufraeumenAsync(CancellationToken abbruch)
    {
        using var bereich = dienste.CreateScope();

        var mandant = bereich.ServiceProvider.GetRequiredService<MandantKontext>();
        mandant.Wartung = true;

        var datenbank = bereich.ServiceProvider.GetRequiredService<ZeltlotseDbContext>();
        var grenze = DateTimeOffset.UtcNow.AddDays(-FreizeitenErweiterungen.PapierkorbTage);

        var freizeiten = await datenbank.Freizeiten
            .IgnoreQueryFilters()
            .Where(f => f.GeloeschtAm != null && f.GeloeschtAm < grenze)
            .ExecuteDeleteAsync(abbruch);

        var organisationen = await datenbank.Organisationen
            .Where(o => o.GeloeschtAm != null && o.GeloeschtAm < grenze)
            .ExecuteDeleteAsync(abbruch);

        if (freizeiten + organisationen > 0)
        {
            protokoll.LogInformation(
                "Papierkorb geleert: {Freizeiten} Freizeiten, {Organisationen} Organisationen.",
                freizeiten, organisationen);
        }
    }
}
