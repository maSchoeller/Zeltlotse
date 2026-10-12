using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Organisationen.Contracts.Loeschfristen;
using Zeltlotse.SharedKernel.Datenschutz;

namespace Zeltlotse.Api.Datenschutz;

/// <summary>
/// Ein Lösch-Lauf (AD-16, NFR-5): liest die Fristen aller Organisationen und das Freizeitende aller Freizeiten, baut daraus den
/// <see cref="LoeschAuftrag"/> und ruft jedes registrierte <see cref="ILoeschlauf"/> in eigenem DI-Scope auf. Scheitert ein Modul,
/// wird das nur mit Kennungen geloggt, und die anderen Module laufen weiter.
/// </summary>
public sealed partial class Loeschlauf
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _uhr;
    private readonly ILogger<Loeschlauf> _logger;

    internal Loeschlauf(IServiceScopeFactory scopes, TimeProvider uhr, ILogger<Loeschlauf> logger)
    {
        _scopes = scopes;
        _uhr = uhr;
        _logger = logger;
    }

    /// <summary>Führt den Lauf einmal aus; liefert den Auftrag, den die Module bekommen haben.</summary>
    public async Task<LoeschAuftrag> LaufAsync(CancellationToken abbruch = default)
    {
        var auftrag = await AuftragAsync(abbruch);
        int anzahl;
        await using (var scope = _scopes.CreateAsyncScope())
        {
            anzahl = scope.ServiceProvider.GetServices<ILoeschlauf>().Count();
        }

        for (var i = 0; i < anzahl; i++)
        {
            await using var scope = _scopes.CreateAsyncScope();
            var modul = scope.ServiceProvider.GetServices<ILoeschlauf>().ElementAt(i);
            try
            {
                await modul.LaufAsync(auftrag, abbruch);
            }
            catch (Exception fehler) when (!abbruch.IsCancellationRequested)
            {
                ModulFehlgeschlagen(modul.GetType().Name, fehler.GetType().Name);
            }
        }

        Gelaufen(auftrag.FreizeitenMitAbgelaufenenDaten.Count, anzahl);
        return auftrag;
    }

    /// <summary>Freizeiten, deren Freizeitende + Frist der Organisation (Monate, Kalender in Europe/Berlin) erreicht ist.</summary>
    private async Task<LoeschAuftrag> AuftragAsync(CancellationToken abbruch)
    {
        var jetzt = _uhr.GetUtcNow();
        await using var scope = _scopes.CreateAsyncScope();
        var fristen = (await scope.ServiceProvider.GetRequiredService<ILoeschfristenLesen>().AlleAsync(abbruch))
            .ToDictionary(f => f.OrganisationId);
        var freizeiten = await scope.ServiceProvider.GetRequiredService<IFreizeitEndenLesen>().AlleAsync(abbruch);
        var abgelaufen = freizeiten
            .Where(f => fristen.TryGetValue(f.OrganisationId, out var frist) && PlusMonate(f.Freizeitende, frist.FreizeitDatenMonate) <= jetzt)
            .Select(f => f.FreizeitId)
            .ToList();
        return new LoeschAuftrag(
            jetzt,
            abgelaufen,
            [.. fristen.Values.Select(f => new NachweisBestaetigungFrist(f.OrganisationId, f.NachweisBestaetigungMonate))]);
    }

    private static DateTimeOffset PlusMonate(DateTimeOffset zeitpunkt, int monate)
    {
        var ortszeit = TimeZoneInfo.ConvertTime(zeitpunkt, Berlin).DateTime.AddMonths(monate);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(ortszeit, DateTimeKind.Unspecified), Berlin), TimeSpan.Zero);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Lösch-Lauf des Moduls {Modul} fehlgeschlagen ({FehlerTyp}).")]
    private partial void ModulFehlgeschlagen(string modul, string fehlerTyp);

    [LoggerMessage(Level = LogLevel.Information, Message = "Lösch-Lauf beendet: {AnzahlFreizeiten} Freizeiten mit abgelaufenen Daten, {AnzahlModule} Module.")]
    private partial void Gelaufen(int anzahlFreizeiten, int anzahlModule);
}

/// <summary>Täglicher Hintergrunddienst des API-Hosts (AD-16): führt den <see cref="Loeschlauf"/> aus.</summary>
public sealed partial class LoeschDienst(Loeschlauf lauf, TimeProvider uhr, ILogger<LoeschDienst> logger) : BackgroundService
{
    private static readonly TimeSpan Takt = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Takt, uhr);
        do
        {
            try
            {
                await lauf.LaufAsync(stoppingToken);
            }
            catch (Exception fehler) when (!stoppingToken.IsCancellationRequested)
            {
                LaufFehlgeschlagen(fehler.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Lösch-Lauf fehlgeschlagen ({FehlerTyp}).")]
    private partial void LaufFehlgeschlagen(string fehlerTyp);
}

/// <summary>Registrierung des Datenschutzes im Host: Auskunft des Sicherheitsprotokolls, Lösch-Lauf und Lösch-Dienst.</summary>
internal static class DatenschutzRegistrierung
{
    /// <summary>Nach den Modulen aufrufen: Die Auskunft des Sicherheitsprotokolls steht dann als letzter Abschnitt.</summary>
    public static IServiceCollection AddDatenschutz(this IServiceCollection services)
    {
        services.AddScoped<IDatenauskunft, SicherheitsprotokollAuskunft>();
        services.AddSingleton(sp => new Loeschlauf(
            sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<Loeschlauf>>()));
        services.AddHostedService<LoeschDienst>();
        return services;
    }
}
