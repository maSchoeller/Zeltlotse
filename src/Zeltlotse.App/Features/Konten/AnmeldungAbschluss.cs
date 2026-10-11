using Microsoft.AspNetCore.Components;
using Zeltlotse.App.Sitzung;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Transport;

namespace Zeltlotse.App.Features.Konten;

/// <summary>
/// Gemeinsamer Schritt nach jeder erfolgreichen Anmeldung (Link, Code, Passwort) und nach dem Abmelden: Antiforgery-Token
/// verwerfen (es hängt an der Identität), nach der Anmeldung <c>POST /api/ich/profil</c> (AD-2: der Client verbindet Konto
/// und Person) und weiter zur Startseite bzw. zur Seite „Anmelden“.
/// </summary>
public sealed class AnmeldungAbschluss(
    AntiforgeryHandler antiforgery, PersonenApiClient personen, KontenApiClient konten, AppSitzung sitzung, NavigationManager navigation)
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

        navigation.NavigateTo("./");
    }

    /// <summary>Abmelden; auch wenn der Aufruf scheitert, endet die Sitzung auf dem Gerät und es geht zur Seite „Anmelden“.</summary>
    public async Task AbmeldenAsync(CancellationToken abbruch = default)
    {
        try
        {
            await konten.AbmeldenAsync(abbruch);
        }
        catch (Exception fehler) when (IstAktionsfehler(fehler))
        {
            // Lokal trotzdem abmelden.
        }

        antiforgery.TokenVerwerfen();
        sitzung.Abmelden();
        navigation.NavigateTo(AppSitzung.AnmeldenPfad);
    }
}
