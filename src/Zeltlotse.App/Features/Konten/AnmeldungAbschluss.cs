using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.App.Browser;
using Zeltlotse.App.Offline;
using Zeltlotse.App.Sitzung;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Transport;

namespace Zeltlotse.App.Features.Konten;

/// <summary>
/// Gemeinsamer Schritt nach jeder erfolgreichen Anmeldung (Link, Code, Passwort) und nach dem Abmelden: Antiforgery-Token
/// verwerfen (es hängt an der Identität), nach der Anmeldung <c>POST /api/ich/profil</c> (AD-2: der Client verbindet Konto
/// und Person) und weiter zum Rückweg (z. B. der Einladung) bzw. zur Startseite; nach dem Abmelden zur Seite „Anmelden“.
/// </summary>
public sealed class AnmeldungAbschluss(
    AntiforgeryHandler antiforgery, PersonenApiClient personen, KontenApiClient konten, AppSitzung sitzung, NavigationManager navigation,
    IServiceProvider dienste)
{
    /// <summary>Text der Meldung „Fehler (Aktion)“ (EXPERIENCE.md), wenn ein Aufruf scheitert (Netz, Zeitüberschreitung, 429, 5xx).</summary>
    public const string FehlerText = "Das hat nicht geklappt. Bitte gleich noch einmal versuchen.";

    /// <summary>Fehler eines API-Aufrufs, den eine Seite selbst als Meldung zeigt, statt die Fehlergrenze auszulösen.</summary>
    public static bool IstAktionsfehler(Exception fehler) => fehler is HttpRequestException or OperationCanceledException;

    /// <summary>
    /// Die Sitzung steht. Scheitert das Anlegen des Profils, geht es trotzdem zur Startseite; die legt es beim Laden an.
    /// </summary>
    public async Task AngemeldetAsync(CancellationToken abbruch = default)
    {
        antiforgery.TokenVerwerfen();
        try
        {
            var profil = await personen.ProfilSicherstellenAsync(abbruch);
            sitzung.Anmelden(profil.Vorname);
        }
        catch (Exception fehler) when (IstAktionsfehler(fehler))
        {
            sitzung.Anmelden(null);
        }

        navigation.NavigateTo(sitzung.RueckwegNehmen() ?? "./");
    }

    /// <summary>
    /// Abmelden; auch wenn der Aufruf scheitert, endet die Sitzung auf dem Gerät und es geht zur Seite „Anmelden“. Mit
    /// <paramref name="rueckweg"/> (relativ zur Basis <c>/app/</c>) geht es nach der nächsten Anmeldung dorthin, z. B. nach der
    /// Übernahme eines Gast-Profils zur Familienübersicht. Story 6-1: Das Push-Abo des Geräts endet mit (der Browser meldet es ab; die Zeilen beim Server
    /// räumt der nächste Push mit 410 ab), damit das Gerät keine Nachrichten aus den Freizeiten der abgemeldeten Person mehr zeigt. Scheitert das, geht
    /// das Abmelden trotzdem weiter. Story 6-5: Die ganze Offline-Datenbank samt Schlüssel wird gelöscht.
    /// </summary>
    public async Task AbmeldenAsync(string? rueckweg = null, CancellationToken abbruch = default)
    {
        try
        {
            await konten.AbmeldenAsync(abbruch);
        }
        catch (Exception fehler) when (IstAktionsfehler(fehler))
        {
            // Lokal trotzdem abmelden.
        }

        await PushBeendenAsync();
        await OfflineLoeschenAsync();
        antiforgery.TokenVerwerfen();
        sitzung.Abmelden(rueckweg);
        navigation.NavigateTo(AppSitzung.AnmeldenPfad);
    }

    /// <summary>Story 6-5: löscht die Offline-Datenbank, wenn es einen Offline-Speicher gibt; Fehler werden übergangen.</summary>
    private async Task OfflineLoeschenAsync()
    {
        if (dienste.GetService<OfflineSpeicher>() is not { } offline)
        {
            return;
        }

        try
        {
            await offline.AllesLoeschenAsync();
        }
        catch (Exception fehler) when (fehler is InvalidOperationException or TaskCanceledException)
        {
            // Ohne Löschen im Browser: Die Sitzung endet trotzdem.
        }
    }

    /// <summary>Story 6-1: beendet das Push-Abo des Browsers, wenn es einen Browser gibt; Fehler werden übergangen.</summary>
    private async Task PushBeendenAsync()
    {
        if (dienste.GetService<IBrowser>() is not { } browser)
        {
            return;
        }

        try
        {
            await browser.PushAbmeldenAsync();
        }
        catch (Exception fehler) when (fehler is InvalidOperationException or TaskCanceledException)
        {
            // Ohne Abmeldung im Browser: Die Sitzung endet trotzdem.
        }
    }
}
