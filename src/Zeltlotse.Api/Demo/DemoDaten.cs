namespace Zeltlotse.Api.Demo;

/// <summary>
/// Spielt die erfundenen Demo-Daten aus <see cref="DemoKatalog"/> ein (NFR-20, Konvention „Anwender-Doku“), wenn der
/// Schalter <see cref="Schalter"/> an ist. Erlaubt ist das nur in <c>Development</c> und <c>Testing</c>; das prüft
/// <see cref="Start.Startpruefung"/> vor den Migrationen. Der Host ruft <see cref="EinspielenAsync"/> direkt nach den
/// Migrationen auf; wiederholtes Einspielen (Neustart mit derselben Datenbank) ist unschädlich.
/// </summary>
/// <remarks>
/// Es gibt noch keine Organisation, Freizeit oder Person in einem Modul. Ab Story 1-1 ergänzt jede Fach-Story hier die
/// Commands ihres Moduls (über Mediator, AD-27) und macht sie idempotent, etwa „nur anlegen, wenn die
/// Demo-Organisation fehlt“. Ein Seed, der direkt in Tabellen schreibt, ist verboten (AD-27).
/// </remarks>
internal sealed partial class DemoDaten(IConfiguration konfiguration, ILogger<DemoDaten> logger)
{
    public const string Schalter = "Zeltlotse:DemoDaten:Aktiv";

    private int _einspielungen;

    /// <summary>Ob der Schalter an ist (Vorgabe: aus).</summary>
    public bool Aktiv => IstAktiv(konfiguration);

    /// <summary>Wie oft dieser Host die Demo-Daten eingespielt hat.</summary>
    public int Einspielungen => _einspielungen;

    public static bool IstAktiv(IConfiguration konfiguration) => konfiguration.GetValue<bool>(Schalter);

    public Task EinspielenAsync(CancellationToken abbruch = default)
    {
        abbruch.ThrowIfCancellationRequested();
        if (!Aktiv)
        {
            return Task.CompletedTask;
        }

        // Noch keine Modul-Commands: Die Fach-Stories ergänzen hier das idempotente Anlegen von Organisation,
        // Freizeit und Personen aus dem Katalog.
        Interlocked.Increment(ref _einspielungen);
        Eingespielt(DemoKatalog.Personen.Count);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo-Daten eingespielt ({AnzahlPersonen} Personen im Katalog).")]
    private partial void Eingespielt(int anzahlPersonen);
}
