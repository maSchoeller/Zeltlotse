using Microsoft.EntityFrameworkCore;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;

namespace Zeltlotse.Api.Betrieb;

/// <summary>Löscht Zeilen des Sicherheitsprotokolls, die älter als die feste Frist sind, endgültig.</summary>
public sealed class SicherheitsprotokollAufraeumen
{
    private readonly IDbContextFactory<BetriebDbContext> _kontexte;
    private readonly TimeProvider _uhr;

    internal SicherheitsprotokollAufraeumen(IDbContextFactory<BetriebDbContext> kontexte, TimeProvider uhr)
    {
        _kontexte = kontexte;
        _uhr = uhr;
    }

    /// <summary>Liefert die Zahl gelöschter Zeilen.</summary>
    public async Task<int> AufraeumenAsync(CancellationToken abbruch = default)
    {
        var grenze = _uhr.GetUtcNow() - SicherheitsprotokollFrist.Dauer;
        await using var db = await _kontexte.CreateDbContextAsync(abbruch);
        return await db.Sicherheitsprotokoll.Where(z => z.Zeitpunkt < grenze).ExecuteDeleteAsync(abbruch);
    }
}

/// <summary>Täglicher Hintergrunddienst des API-Hosts: räumt das Sicherheitsprotokoll nach der Frist auf (AD-22).</summary>
public sealed partial class SicherheitsprotokollFristDienst(
    SicherheitsprotokollAufraeumen aufraeumen,
    TimeProvider uhr,
    ILogger<SicherheitsprotokollFristDienst> logger) : BackgroundService
{
    private static readonly TimeSpan Takt = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Takt, uhr);
        do
        {
            try
            {
                var geloescht = await aufraeumen.AufraeumenAsync(stoppingToken);
                Aufgeraeumt(geloescht);
            }
            catch (Exception fehler) when (!stoppingToken.IsCancellationRequested)
            {
                AufraeumenFehlgeschlagen(fehler.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Sicherheitsprotokoll aufgeräumt: {Anzahl} Zeilen nach Ablauf der Frist gelöscht.")]
    private partial void Aufgeraeumt(int anzahl);

    [LoggerMessage(Level = LogLevel.Error, Message = "Aufräumen des Sicherheitsprotokolls fehlgeschlagen ({FehlerTyp}).")]
    private partial void AufraeumenFehlgeschlagen(string fehlerTyp);
}
