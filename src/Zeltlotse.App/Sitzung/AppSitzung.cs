using Microsoft.AspNetCore.Components;
using Zeltlotse.App.Offline;
using Zeltlotse.Ui.Transport;

namespace Zeltlotse.App.Sitzung;

/// <summary>
/// Sitzung aus Sicht der Web-App (AD-12): ob angemeldet, der Vorname für den Konto-Link der App-Leiste und der Rückweg
/// nach der Anmeldung. Die Sitzung selbst ist nur das HttpOnly-Cookie; hier liegt kein Token im Browser-Speicher (kein
/// globaler Zustandsspeicher, nur diese Werte im Speicher der laufenden App).
/// Eine <c>401</c>-Antwort (über den <see cref="SitzungsHandler"/>) führt zur Seite „Anmelden“ und merkt sich die Seite,
/// auf der sie kam, als Rückweg. Story 6-5: Eine abgelaufene Sitzung löscht vorher die ganze Offline-Datenbank samt Schlüssel (die Daten sind an die
/// Sitzung gebunden); ohne Offline-Speicher (Tests) entfällt das.
/// </summary>
public sealed class AppSitzung(NavigationManager navigation, OfflineSpeicher? offline = null) : ISitzung
{
    /// <summary>Pfad der Seite „Anmelden“ relativ zur Basis <c>/app/</c>.</summary>
    public const string AnmeldenPfad = "anmelden";

    private string? _rueckweg;

    /// <summary>Wird bei jeder Änderung von <see cref="Angemeldet"/> oder <see cref="Vorname"/> ausgelöst.</summary>
    public event Action? Geaendert;

    public bool Angemeldet { get; private set; }

    public string? Vorname { get; private set; }

    /// <summary>
    /// Die Seite (relativ zur Basis <c>/app/</c>), auf die es nach der Anmeldung per Code oder Passwort zurückgeht, z. B.
    /// die Einladung; nur im Speicher, nie im Browser-Speicher oder in der Adresse.
    /// </summary>
    public string? Rueckweg => _rueckweg;

    /// <summary>Eine Seite hat das eigene Profil geladen: angemeldet.</summary>
    public void Anmelden(string? vorname)
    {
        Angemeldet = true;
        Vorname = string.IsNullOrWhiteSpace(vorname) ? null : vorname;
        Geaendert?.Invoke();
    }

    /// <summary>Ausdrückliches Abmelden: Sitzung und Rückweg sind weg.</summary>
    public void Abmelden() => Abmelden(null);

    /// <summary>
    /// Ausdrückliches Abmelden: Die Sitzung ist weg, der Rückweg auch, außer einem neuen eigenen Pfad <paramref name="rueckweg"/> für
    /// die nächste Anmeldung.
    /// </summary>
    public void Abmelden(string? rueckweg)
    {
        _rueckweg = rueckweg is not null && IstEigenerPfad(rueckweg) ? rueckweg : null;
        Zuruecksetzen();
    }

    /// <summary>Liefert den Rückweg und vergisst ihn; <c>null</c>, wenn es keinen gibt.</summary>
    public string? RueckwegNehmen()
    {
        var rueckweg = _rueckweg;
        _rueckweg = null;
        return rueckweg;
    }

    /// <summary>
    /// Abgemeldet (Story 6-5: Offline-Daten gelöscht) und, außer auf den Anmelde-Seiten selbst und (Story 6-4) im Teilnehmer-Zugang, weiter zur Seite „Anmelden“. Die aktuelle Seite wird der
    /// Rückweg; nur Pfade innerhalb der Web-App, nie eine fremde Adresse.
    /// </summary>
    public async ValueTask AbgelaufenAsync()
    {
        if (offline is not null)
        {
            await offline.AllesLoeschenAsync();
        }

        Zuruecksetzen();
        var pfad = navigation.ToBaseRelativePath(navigation.Uri);
        // Story 6-4: Der Teilnehmer-Zugang kennt keine Anmeldung; seine Seiten bleiben, wo sie sind.
        if (!pfad.StartsWith(AnmeldenPfad, StringComparison.OrdinalIgnoreCase) && !IstTeilnehmerPfad(pfad))
        {
            _rueckweg = IstEigenerPfad(pfad) ? pfad : null;
            navigation.NavigateTo(AnmeldenPfad);
        }
    }

    /// <summary>Story 6-4: eine Seite des Teilnehmer-Zugangs (<c>teilnehmer</c>, <c>teilnehmer/{token}</c>, mit oder ohne Abfrage).</summary>
    public static bool IstTeilnehmerPfad(string pfad)
    {
        var ohneAbfrage = pfad.Split('?', '#')[0];
        return ohneAbfrage.Equals(TeilnehmerPfad, StringComparison.OrdinalIgnoreCase)
               || ohneAbfrage.StartsWith(TeilnehmerPfad + "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Story 6-4: Pfad des Teilnehmer-Zugangs relativ zur Basis <c>/app/</c>.</summary>
    public const string TeilnehmerPfad = "teilnehmer";

    /// <summary>Ein relativer Pfad ohne Schema, Host oder führenden Schrägstrich; die Startseite braucht keinen Rückweg.</summary>
    private static bool IstEigenerPfad(string pfad) =>
        pfad.Length > 0
        && !pfad.StartsWith('/')
        && !pfad.Contains(':', StringComparison.Ordinal)
        && !pfad.Contains("//", StringComparison.Ordinal)
        && !pfad.Contains('\\', StringComparison.Ordinal);

    private void Zuruecksetzen()
    {
        Angemeldet = false;
        Vorname = null;
        Geaendert?.Invoke();
    }
}
