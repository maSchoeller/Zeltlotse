namespace Zeltlotse.Api.Contracts.Organisationen;

/// <summary>
/// Antwort von <c>GET /api/organisationen/{orgId}/loeschfristen</c> (Org-Admin, NFR-5): nach wie vielen Monaten Zeltlotse Daten
/// einer Freizeit (ab Freizeitende) und abgelaufene Nachweis-Bestätigungen (ab „gültig bis“) löscht.
/// </summary>
public sealed record LoeschfristenAntwort(uint Version, int FreizeitDatenMonate, int NachweisBestaetigungMonate);

/// <summary>Bearbeitbare Felder der Löschfristen für <c>PATCH /api/organisationen/{orgId}/loeschfristen</c> (AD-19).</summary>
public enum LoeschfristFeld
{
    FreizeitDatenMonate,
    NachweisBestaetigungMonate,
}

/// <summary>JSON-Namen der Löschfristen-Felder. Werte stehen als Text: eine Zahl zwischen <see cref="Mindestens"/> und <see cref="Hoechstens"/>.</summary>
public static class LoeschfristFelder
{
    public const string FreizeitDatenMonate = "freizeitDatenMonate";

    public const string NachweisBestaetigungMonate = "nachweisBestaetigungMonate";

    public const int Mindestens = 1;

    public const int Hoechstens = 120;

    public static string JsonName(LoeschfristFeld feld) => feld switch
    {
        LoeschfristFeld.FreizeitDatenMonate => FreizeitDatenMonate,
        LoeschfristFeld.NachweisBestaetigungMonate => NachweisBestaetigungMonate,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };
}
