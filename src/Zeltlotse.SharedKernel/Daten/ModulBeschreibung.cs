namespace Zeltlotse.SharedKernel.Daten;

/// <summary>Meldet ein Modul beim Host an: Name, Schema und schreibender DbContext (für Migrationen und Zustellung).</summary>
public sealed record ModulBeschreibung(string Name, string Schema, Type DbContextTyp);
