namespace Zeltlotse.Ui.Komponenten;

/// <summary>
/// Kennzeichnet einen Baustein, der Ereignis-Handler braucht und deshalb nur interaktiv (Web-App) funktioniert (AD-29).
/// Die öffentlichen Seiten (statisches Server-Rendering) verwenden keinen so gekennzeichneten Baustein;
/// ein Architekturtest prüft das. Nur solche Bausteine haben Parameter vom Typ <c>EventCallback</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class NurInteraktivAttribute : Attribute;
