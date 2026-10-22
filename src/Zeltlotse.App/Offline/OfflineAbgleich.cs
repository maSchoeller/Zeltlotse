using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Sicherheit;
using Zeltlotse.App.Features.Konten;
using Zeltlotse.App.Sitzung;
using Zeltlotse.App.Verbindung;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Kontext;

namespace Zeltlotse.App.Offline;

/// <summary>
/// Story 6-5: Der Offline-Abgleich der Web-App (FR-65, AD-18). Bei Verbindung lädt er für jede eigene, laufende Freizeit (Rolle Leitung oder Team in
/// <c>GET /api/ich/freizeiten</c>) Kopf und Kontext, Sicherheitskette, mit „freizeitpaesse-sehen“ die Notfallkarte und mit dem Schalter Verpflegung die
/// Tagesansichten aller Tage in den <see cref="OfflineSpeicher"/>; davor sendet er die Warteschlange des Vorfall-Protokolls (idempotent über die Kennung
/// vom Gerät). Antwortet die API für eine Freizeit mit <c>403</c>/<c>404</c> oder ist sie nicht mehr laufend gelistet, löscht er deren Daten samt
/// wartender Einträge. Ein Netzfehler beendet den Lauf; bisher Gespeichertes bleibt. Ausgelöst beim Start mit Sitzung (angemeldet oder schon
/// Gespeichertes), nach dem Login und wenn die Verbindung zurückkommt; danach fragt er <c>navigator.storage.persist()</c> an. Hält die Zahl der
/// wartenden Einträge (<see cref="Wartend"/>) und die gespeicherten Freizeiten (<see cref="Gespeichert"/>) für Offline-Leiste und Notfall-Box.
/// </summary>
public sealed class OfflineAbgleich(
    OfflineSpeicher speicher, FreizeitenApiClient freizeiten, FreizeitKontextDienst kontexte, SicherheitApiClient sicherheit, PersonenApiClient personen,
    KuecheApiClient kueche, AppVerbindung verbindung, AppSitzung sitzung) : IDisposable
{
    private Task? _start;
    private Task? _lauf;
    private bool _warOffline;
    private bool _warAngemeldet;
    private int _generation;

    /// <summary>Wird ausgelöst, wenn sich <see cref="Wartend"/> oder <see cref="Gespeichert"/> geändert haben können.</summary>
    public event Action? Geaendert;

    /// <summary>Wie viele Einträge des Vorfall-Protokolls auf Verbindung warten.</summary>
    public int Wartend { get; private set; }

    /// <summary>Die gespeicherten Freizeiten, nach Name geordnet.</summary>
    public IReadOnlyList<OfflineFreizeit> Gespeichert { get; private set; } = [];

    /// <summary>Ein Fehler ohne Antwort des Servers (kein Netz, Zeitüberschreitung): Dann gilt der Offline-Speicher.</summary>
    public static bool IstNetzfehler(Exception fehler) => fehler is HttpRequestException { StatusCode: null } or TaskCanceledException;

    /// <summary>
    /// Räumt Abgelaufenes auf, liest den Stand und beobachtet Verbindung und Sitzung; mit Verbindung und Sitzung (angemeldet oder schon Gespeichertes)
    /// startet der erste Abgleich im Hintergrund. Mehrfacher Aufruf startet nur einmal.
    /// </summary>
    public Task StartenAsync() => _start ??= StartAsync();

    private async Task StartAsync()
    {
        _warOffline = verbindung.Offline;
        _warAngemeldet = sitzung.Angemeldet;
        verbindung.Geaendert += VerbindungGeaendert;
        sitzung.Geaendert += SitzungGeaendert;
        await AktualisierenAsync();
        if (!verbindung.BrowserOffline && (sitzung.Angemeldet || Gespeichert.Count > 0 || Wartend > 0))
        {
            _ = AbgleichenAsync();
        }
    }

    /// <summary>Ein Abgleich; läuft schon einer, liefert es diesen.</summary>
    public Task AbgleichenAsync() => _lauf is { IsCompleted: false } ? _lauf : _lauf = LaufAsync();

    /// <summary>Legt einen Eintrag in die Warteschlange; <c>false</c>, wenn die Freizeit nicht gespeichert ist (dann wird nichts gespeichert).</summary>
    public async Task<bool> VorfallEinreihenAsync(Guid freizeitId, VorfallEintragNeu eintrag)
    {
        var eingereiht = await speicher.VorfallEinreihenAsync(freizeitId, eintrag);
        await AktualisierenAsync();
        return eingereiht;
    }

    /// <summary>Räumt Abgelaufenes auf und liest Freizeiten und Warteschlange neu.</summary>
    public async Task AktualisierenAsync()
    {
        await speicher.AufraeumenAsync();
        Gespeichert = await speicher.FreizeitenAsync();
        Wartend = (await speicher.WartendeAsync()).Count;
        Geaendert?.Invoke();
    }

    private async Task LaufAsync()
    {
        // Erst zurückgeben, dann laufen: So ist der Lauf gemerkt, bevor eine Meldung der Verbindung während des Laufs einen zweiten auslösen könnte.
        await Task.Yield();
        _generation = speicher.Generation;
        try
        {
            await speicher.AufraeumenAsync();
            // Nach einem Netzfehler versucht er es trotzdem: Der Lauf ist selbst der Versuch, ob die Verbindung wieder steht.
            if (verbindung.BrowserOffline || !await WarteschlangeSendenAsync())
            {
                return;
            }

            var laufend = (await freizeiten.MeineLadenAsync())
                .Where(f => f.Phase == Phase.Laeuft && f.Rollen.Any(r => r is FreizeitRolle.Leitung or FreizeitRolle.Team))
                .Select(f => f.FreizeitId)
                .ToHashSet();
            foreach (var gespeichert in await speicher.FreizeitenAsync())
            {
                if (!laufend.Contains(gespeichert.Freizeit.Id))
                {
                    Weiter();
                    await speicher.FreizeitLoeschenAsync(gespeichert.Freizeit.Id);
                }
            }

            foreach (var freizeitId in laufend)
            {
                await FreizeitAbgleichenAsync(freizeitId);
            }

            Weiter();

            await speicher.SpeicherHaltenAsync();
        }
        catch (Exception fehler) when (AnmeldungAbschluss.IstAktionsfehler(fehler))
        {
            // Netz weg oder Server gestört: Was bis hierher gespeichert ist, bleibt; der nächste Abgleich macht weiter.
        }
        finally
        {
            await AktualisierenAsync();
        }
    }

    /// <summary>
    /// Wurde die Offline-Datenbank seit dem Beginn des Laufs gelöscht (Abmelden, Sitzung abgelaufen), bricht der Lauf ab, statt Daten und Schlüssel neu
    /// anzulegen.
    /// </summary>
    private void Weiter()
    {
        if (speicher.Generation != _generation)
        {
            throw new OperationCanceledException("Die Offline-Daten wurden während des Abgleichs gelöscht.");
        }
    }

    /// <summary>Sendet die Warteschlange; <c>false</c>, wenn ein Eintrag warten muss (Netz, Serverfehler).</summary>
    private async Task<bool> WarteschlangeSendenAsync()
    {
        var geloescht = new HashSet<Guid>();
        foreach (var wartend in await speicher.WartendeAsync())
        {
            if (geloescht.Contains(wartend.FreizeitId))
            {
                continue;
            }

            switch (await sicherheit.VorfallAnlegenAsync(wartend.FreizeitId, wartend.Eintrag))
            {
                case VorfallAnlegenErgebnis.Erfolg:
                case VorfallAnlegenErgebnis.Abgeschlossen:
                case VorfallAnlegenErgebnis.Abgelehnt:
                    // Angenommen oder endgültig abgelehnt: Sonst bliebe der Eintrag ewig hängen.
                    Weiter();
                    await speicher.VorfallEntfernenAsync(wartend.FreizeitId, wartend.Eintrag.Id);
                    break;
                case VorfallAnlegenErgebnis.NichtVerfuegbar:
                    Weiter();
                    await speicher.FreizeitLoeschenAsync(wartend.FreizeitId);
                    geloescht.Add(wartend.FreizeitId);
                    break;
                default:
                    return false;
            }

            await AktualisierenAsync();
        }

        return true;
    }

    private async Task FreizeitAbgleichenAsync(Guid freizeitId)
    {
        var kontext = await kontexte.LadenAsync(freizeitId);
        var freizeit = kontext is not null && OfflineSpeicher.Speicherbar(kontext) ? await freizeiten.LadenAsync(freizeitId) : null;
        if (kontext is null || freizeit is null)
        {
            Weiter();
            await speicher.FreizeitLoeschenAsync(freizeitId);
            return;
        }

        Weiter();

        await speicher.FreizeitSchreibenAsync(new OfflineFreizeit(freizeit, kontext));
        if (await sicherheit.SicherheitsketteLadenAsync(freizeitId) is not { } kette)
        {
            Weiter();
            await speicher.FreizeitLoeschenAsync(freizeitId);
            return;
        }

        Weiter();

        await speicher.SicherheitsketteSchreibenAsync(freizeit, kette);
        if (FreizeitKontextDienst.Darf(kontext, Aktionsschluessel.FreizeitpaesseSehen))
        {
            if (await personen.NotfallkarteLadenAsync(freizeitId) is not { } karte)
            {
                Weiter();
                await speicher.FreizeitLoeschenAsync(freizeitId);
                return;
            }

            Weiter();

            await speicher.NotfallkarteSchreibenAsync(freizeit, karte);
        }
        else
        {
            Weiter();
            await speicher.NotfallkarteEntfernenAsync(freizeitId);
        }

        if (!kontext.Schalter.Contains(FunktionsSchalter.Verpflegung))
        {
            Weiter();
            await speicher.TagesansichtenEntfernenAsync(freizeitId);
            return;
        }

        for (var tag = freizeit.ErsterTag; tag <= freizeit.LetzterTag; tag = tag.AddDays(1))
        {
            if (await kueche.TagesansichtLadenAsync(freizeitId, tag) is not { } ansicht)
            {
                Weiter();
                await speicher.FreizeitLoeschenAsync(freizeitId);
                return;
            }

            Weiter();

            await speicher.TagesansichtSchreibenAsync(freizeit, ansicht);
        }
    }

    /// <summary>Kommt die Verbindung zurück, gleicht er ab (auch die Warteschlange).</summary>
    private void VerbindungGeaendert()
    {
        var offline = verbindung.Offline;
        if (_warOffline && !offline)
        {
            _ = AbgleichenAsync();
        }

        _warOffline = offline;
    }

    /// <summary>Nach dem Login gleicht er ab; nach dem Abmelden ist der Speicher schon gelöscht, der Stand wird neu gelesen.</summary>
    private void SitzungGeaendert()
    {
        var angemeldet = sitzung.Angemeldet;
        if (!_warAngemeldet && angemeldet && !verbindung.BrowserOffline)
        {
            _ = AbgleichenAsync();
        }
        else if (!angemeldet)
        {
            _ = AktualisierenAsync();
        }

        _warAngemeldet = angemeldet;
    }

    public void Dispose()
    {
        verbindung.Geaendert -= VerbindungGeaendert;
        sitzung.Geaendert -= SitzungGeaendert;
    }
}
