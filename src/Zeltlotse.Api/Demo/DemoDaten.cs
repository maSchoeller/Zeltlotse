using Mediator;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Konten.Anmelden;
using Zeltlotse.Personen.Profil;

namespace Zeltlotse.Api.Demo;

/// <summary>
/// Spielt die erfundenen Demo-Daten aus <see cref="DemoKatalog"/> ein (NFR-20, Konvention „Anwender-Doku“), wenn der
/// Schalter <see cref="Schalter"/> an ist. Erlaubt ist das nur in <c>Development</c> und <c>Testing</c>; das prüft
/// <see cref="Start.Startpruefung"/> vor den Migrationen. Der Host ruft <see cref="EinspielenAsync"/> direkt nach den
/// Migrationen auf; wiederholtes Einspielen (Neustart mit derselben Datenbank) ist unschädlich.
/// </summary>
/// <remarks>
/// Eingespielt wird nur über die Commands der Module (Mediator, AD-27), nie direkt in Tabellen. Jede Fach-Story ergänzt
/// hier ihre Commands und macht sie idempotent. Bisher (Story 1-1): je Demo-Person mit Mail ein Konto und ein Profil mit
/// Vor- und Nachnamen; eine vorhandene Mail-Adresse wird übersprungen. Kinder ohne Mail bekommen hier nichts (Familie: 1-4).
/// </remarks>
internal sealed partial class DemoDaten(IConfiguration konfiguration, IServiceScopeFactory scopes, ILogger<DemoDaten> logger)
{
    public const string Schalter = "Zeltlotse:DemoDaten:Aktiv";

    private int _einspielungen;

    /// <summary>Ob der Schalter an ist (Vorgabe: aus).</summary>
    public bool Aktiv => IstAktiv(konfiguration);

    /// <summary>Wie oft dieser Host die Demo-Daten eingespielt hat.</summary>
    public int Einspielungen => _einspielungen;

    public static bool IstAktiv(IConfiguration konfiguration) => konfiguration.GetValue<bool>(Schalter);

    public async Task EinspielenAsync(CancellationToken abbruch = default)
    {
        abbruch.ThrowIfCancellationRequested();
        if (!Aktiv)
        {
            return;
        }

        var neu = 0;
        foreach (var person in DemoKatalog.Personen.Where(p => p.Mail is not null))
        {
            if (await KontoUndProfilAsync(person, abbruch))
            {
                neu++;
            }
        }

        Interlocked.Increment(ref _einspielungen);
        Eingespielt(DemoKatalog.Personen.Count, neu);
    }

    /// <summary>Konto und Profil einer Demo-Person; <c>false</c>, wenn es das Konto schon gab.</summary>
    private async Task<bool> KontoUndProfilAsync(DemoPerson person, CancellationToken abbruch)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var konto = await sender.Send(new KontoSicherstellen(person.Mail!), abbruch);
        if (!konto.Neu)
        {
            return false;
        }

        var profil = await sender.Send(new ProfilSicherstellen(konto.PersonId), abbruch);
        var version = profil.Version;
        foreach (var (feld, wert) in new[] { (ProfilFeld.Vorname, person.Vorname), (ProfilFeld.Nachname, person.Nachname) })
        {
            var ergebnis = await sender.Send(new ProfilFeldAendern(konto.PersonId, feld, version, null, wert), abbruch);
            version = ergebnis is ProfilAenderung.Gespeichert gespeichert
                ? gespeichert.Person.Version
                : throw new InvalidOperationException($"Demo-Profil: {feld} ließ sich nicht setzen.");
        }

        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo-Daten eingespielt ({AnzahlPersonen} Personen im Katalog, {NeueKonten} neue Konten).")]
    private partial void Eingespielt(int anzahlPersonen, int neueKonten);
}
