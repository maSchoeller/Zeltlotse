namespace Zeltlotse.SharedKernel.Datenschutz;

/// <summary>
/// Kennzeichnet ein besonders schützenswertes Feld (AD-14), z. B. Freizeitpass-Antworten, Nachweise, Vorfall-Einträge,
/// Team-Notizen. Die Eigenschaft hat immer den Typ <see cref="Geschuetzt"/>; ein Architekturtest prüft beides.
/// In verschlüsselten Feldern wird nicht per Datenbank gesucht.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class BesondersAttribute : Attribute;
