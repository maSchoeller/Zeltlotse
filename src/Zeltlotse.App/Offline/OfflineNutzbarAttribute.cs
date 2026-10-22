namespace Zeltlotse.App.Offline;

/// <summary>
/// Story 6-5: Kennzeichnet eine Seite, die offline nutzbar ist (EXPERIENCE.md → State Patterns „Offline“): Küchen-Tagesansicht, Notfallkarte,
/// Sicherheitskette und Vorfall-Protokoll. Das Layout graut sie offline nicht aus; alle anderen Seiten zeigen „Offline nicht verfügbar.“.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class OfflineNutzbarAttribute : Attribute
{
    /// <summary>Name des kaskadierten Werts, den <c>App.razor</c> aus der Seite der Route setzt.</summary>
    public const string Kaskade = "OfflineNutzbar";

    /// <summary>Ob die Seite (Typ der Route) offline nutzbar ist.</summary>
    public static bool Gilt(Type? seite) => seite is not null && IsDefined(seite, typeof(OfflineNutzbarAttribute), inherit: false);
}
