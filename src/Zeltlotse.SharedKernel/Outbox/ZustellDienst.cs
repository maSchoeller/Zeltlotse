using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Zeltlotse.SharedKernel.Outbox;

/// <summary>Periodischer Hintergrunddienst im API-Host: stellt die Outboxen aller Module zu (AD-4). Kein Nachrichtenbroker.</summary>
public sealed partial class ZustellDienst(IOutboxZustellung zustellung, TimeProvider uhr, ILogger<ZustellDienst> logger) : BackgroundService
{
    private static readonly TimeSpan Takt = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Takt, uhr);
        do
        {
            try
            {
                await zustellung.AllesZustellenAsync(stoppingToken);
            }
            catch (Exception fehler) when (!stoppingToken.IsCancellationRequested)
            {
                DurchgangFehlgeschlagen(fehler.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Zustell-Durchgang fehlgeschlagen ({FehlerTyp}).")]
    private partial void DurchgangFehlgeschlagen(string fehlerTyp);
}
