using Microsoft.AspNetCore.Components;
using Zeltlotse.Ui.Transport;

namespace Zeltlotse.App.Sitzung;

/// <summary>
/// Sitzung aus Sicht der Web-App (AD-12): ob angemeldet und der Vorname für den Konto-Link der App-Leiste. Die Sitzung
/// selbst ist nur das HttpOnly-Cookie; hier liegt kein Token (kein globaler Zustandsspeicher, nur diese zwei Werte).
/// Eine <c>401</c>-Antwort (über den <see cref="SitzungsHandler"/>) führt zur Seite „Anmelden“.
/// </summary>
public sealed class AppSitzung(NavigationManager navigation) : ISitzung
{
    /// <summary>Pfad der Seite „Anmelden“ relativ zur Basis <c>/app/</c>.</summary>
    public const string AnmeldenPfad = "anmelden";

    /// <summary>Wird bei jeder Änderung von <see cref="Angemeldet"/> oder <see cref="Vorname"/> ausgelöst.</summary>
    public event Action? Geaendert;

    public bool Angemeldet { get; private set; }

    public string? Vorname { get; private set; }

    /// <summary>Eine Seite hat das eigene Profil geladen: angemeldet.</summary>
    public void Anmelden(string? vorname)
    {
        Angemeldet = true;
        Vorname = string.IsNullOrWhiteSpace(vorname) ? null : vorname;
        Geaendert?.Invoke();
    }

    public void Abmelden()
    {
        Angemeldet = false;
        Vorname = null;
        Geaendert?.Invoke();
    }

    /// <summary>Abgemeldet und, außer auf den Anmelde-Seiten selbst, weiter zur Seite „Anmelden“.</summary>
    public ValueTask AbgelaufenAsync()
    {
        Abmelden();
        var pfad = navigation.ToBaseRelativePath(navigation.Uri);
        if (!pfad.StartsWith(AnmeldenPfad, StringComparison.OrdinalIgnoreCase))
        {
            navigation.NavigateTo(AnmeldenPfad);
        }

        return ValueTask.CompletedTask;
    }
}
