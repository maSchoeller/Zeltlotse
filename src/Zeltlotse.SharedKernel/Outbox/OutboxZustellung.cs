using System.Text.Json;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.SharedKernel.Mail;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.SharedKernel.Outbox;

/// <summary>Stellt Outbox-Ereignisse den angemeldeten Empfängern zu (AD-4).</summary>
public interface IOutboxZustellung
{
    /// <summary>Ein Durchgang über die Outboxen aller Module; liefert die Zahl erfolgreicher Zustellungen.</summary>
    Task<int> ZustellenAsync(CancellationToken abbruch = default);

    /// <summary>Wiederholt Durchgänge, bis keine fällige Zustellung mehr gelingt („Outbox jetzt zustellen“).</summary>
    Task AllesZustellenAsync(CancellationToken abbruch = default);
}

/// <summary>
/// Zustellung je (Ereignis, Empfänger): jeder Empfänger läuft in eigenem DI-Scope mit dem Mandanten-Kontext
/// aus dem Umschlag; ein Fehler bei einem Empfänger hält die anderen nicht auf und wird mit wachsender
/// Wartezeit wiederholt. Je (Datensatz, Empfänger) wird in der Reihenfolge der Folgenummer zugestellt. Mail-Aufträge
/// (Empfänger <see cref="MailAuftraege.Empfaenger"/>) baut der registrierte <see cref="IMailAufbau{TAuftrag}"/> erst hier zur Mail
/// und <see cref="IMailVersand"/> verschickt sie; ohne Inbox und ohne Command.
/// </summary>
internal sealed partial class OutboxZustellung(
    IServiceScopeFactory scopes,
    IEnumerable<ModulBeschreibung> module,
    EreignisKatalog katalog,
    MailKatalog mailKatalog,
    TimeProvider uhr,
    ILogger<OutboxZustellung> logger) : IOutboxZustellung
{
    private const int Stapelgroesse = 100;
    private const int HoechstzahlDurchgaenge = 1000;
    private static readonly TimeSpan ErsteWartezeit = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LaengsteWartezeit = TimeSpan.FromHours(1);

    private readonly IReadOnlyList<ModulBeschreibung> _module = [.. module];

    public async Task<int> ZustellenAsync(CancellationToken abbruch = default)
    {
        var erfolgreich = 0;
        foreach (var modul in _module)
        {
            try
            {
                erfolgreich += await ModulZustellenAsync(modul, abbruch);
            }
            catch (Exception fehler) when (!abbruch.IsCancellationRequested)
            {
                ModulDurchgangFehlgeschlagen(modul.Name, fehler.GetType().Name);
            }
        }

        return erfolgreich;
    }

    public async Task AllesZustellenAsync(CancellationToken abbruch = default)
    {
        for (var durchgang = 0; durchgang < HoechstzahlDurchgaenge; durchgang++)
        {
            if (await ZustellenAsync(abbruch) == 0)
            {
                return;
            }
        }
    }

    internal static TimeSpan Wartezeit(int versuche)
    {
        var faktor = Math.Pow(2, Math.Min(versuche - 1, 20));
        var wartezeit = TimeSpan.FromTicks((long)(ErsteWartezeit.Ticks * faktor));
        return wartezeit < LaengsteWartezeit ? wartezeit : LaengsteWartezeit;
    }

    private async Task<int> ModulZustellenAsync(ModulBeschreibung modul, CancellationToken abbruch)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = (ModulDbContext)scope.ServiceProvider.GetRequiredService(modul.DbContextTyp);
        var jetzt = uhr.GetUtcNow();

        var faellige = await (
                from z in db.Zustellungen
                join o in db.Outbox on z.EreignisId equals o.Id
                where z.ErledigtAm == null && z.NaechsterVersuch <= jetzt
                where !(from z2 in db.Zustellungen
                        join o2 in db.Outbox on z2.EreignisId equals o2.Id
                        where z2.ErledigtAm == null
                              && z2.Empfaenger == z.Empfaenger
                              && o2.DatensatzId == o.DatensatzId
                              && o2.Folgenummer < o.Folgenummer
                        select z2).Any()
                orderby o.Zeitpunkt, o.Folgenummer, z.Empfaenger
                select new { Zustellung = z, Nachricht = o })
            .Take(Stapelgroesse)
            .ToListAsync(abbruch);

        // Die Abfrage liefert je (Datensatz, Empfänger) höchstens das früheste offene Ereignis,
        // ein späteres wartet also, bis das frühere zugestellt ist.
        var erfolgreich = 0;
        foreach (var eintrag in faellige)
        {
            var (zustellung, nachricht) = (eintrag.Zustellung, eintrag.Nachricht);
            try
            {
                await EinzelnZustellenAsync(nachricht, zustellung.Empfaenger, abbruch);
                zustellung.ErledigtAm = uhr.GetUtcNow();
                zustellung.LetzterFehler = null;
                erfolgreich++;
            }
            catch (Exception fehler) when (!abbruch.IsCancellationRequested)
            {
                zustellung.Versuche++;
                zustellung.NaechsterVersuch = uhr.GetUtcNow() + Wartezeit(zustellung.Versuche);
                zustellung.LetzterFehler = fehler.GetType().Name;
                ZustellungFehlgeschlagen(zustellung.Empfaenger, nachricht.Id, zustellung.Versuche, zustellung.LetzterFehler);
            }

            await db.SaveChangesAsync(abbruch);
        }

        return erfolgreich;
    }

    private async Task EinzelnZustellenAsync(OutboxNachricht nachricht, string empfaenger, CancellationToken abbruch)
    {
        if (empfaenger == MailAuftraege.Empfaenger)
        {
            await MailZustellenAsync(nachricht, abbruch);
            return;
        }

        var eintrag = katalog.Finden(nachricht.Typ, empfaenger)
                      ?? throw new EmpfaengerUnbekanntException();
        var ereignis = JsonSerializer.Deserialize(nachricht.Inhalt, eintrag.EreignisTyp, ModulDbContext.JsonOptionen)
                       ?? throw new JsonException("Leeres Ereignis in der Outbox.");
        var umschlag = new Umschlag(nachricht.Id, nachricht.Zeitpunkt, nachricht.OrganisationId, nachricht.DatensatzId, nachricht.Folgenummer);

        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(umschlag.OrganisationId);
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(eintrag.Erzeugen(umschlag, ereignis), abbruch);
    }

    /// <summary>
    /// Baut die Mail eines Auftrags im eigenen Scope mit dem Mandanten aus dem Umschlag und verschickt sie; <c>null</c> heißt
    /// „nichts zu senden“, die Zustellung ist dann erledigt.
    /// </summary>
    private async Task MailZustellenAsync(OutboxNachricht nachricht, CancellationToken abbruch)
    {
        var eintrag = mailKatalog.Finden(nachricht.Typ) ?? throw new EmpfaengerUnbekanntException();
        var auftrag = JsonSerializer.Deserialize(nachricht.Inhalt, eintrag.AuftragTyp, ModulDbContext.JsonOptionen)
                      ?? throw new JsonException("Leerer Mail-Auftrag in der Outbox.");

        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(nachricht.OrganisationId);
        var inhalt = await eintrag.Aufbauen(scope.ServiceProvider, auftrag, abbruch);
        if (inhalt is null)
        {
            return;
        }

        await scope.ServiceProvider.GetRequiredService<IMailVersand>().SendenAsync(inhalt.An, inhalt.Betreff, inhalt.Text, abbruch);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Zustellung an {Empfaenger} für Ereignis {EreignisId} fehlgeschlagen (Versuch {Versuche}, {FehlerTyp}).")]
    private partial void ZustellungFehlgeschlagen(string empfaenger, Guid ereignisId, int versuche, string fehlerTyp);

    [LoggerMessage(Level = LogLevel.Error, Message = "Zustell-Durchgang für Modul {Modul} fehlgeschlagen ({FehlerTyp}).")]
    private partial void ModulDurchgangFehlgeschlagen(string modul, string fehlerTyp);

    private sealed class EmpfaengerUnbekanntException() : Exception("Für dieses Ereignis ist der Empfänger nicht (mehr) angemeldet.");
}
