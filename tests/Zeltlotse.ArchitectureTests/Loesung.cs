using System.Reflection;

namespace Zeltlotse.ArchitectureTests;

/// <summary>Feste Angaben aus der ARCHITECTURE-SPINE.md (AD-1, AD-2) und Wege in die Lösung.</summary>
internal static class Loesung
{
    /// <summary>Die zehn Module aus AD-1 mit Projektname (ohne Umlaute) und Schema.</summary>
    public static readonly IReadOnlyList<Modul> Module =
    [
        new("Konten", "konten", "Konten"),
        new("Personen", "personen", "Personen"),
        new("Organisationen", "organisationen", "Organisationen"),
        new("Freizeiten", "freizeiten", "Freizeiten"),
        new("Sicherheit", "sicherheit", "Sicherheit"),
        new("Programm", "programm", "Programm"),
        new("Inventar", "inventar", "Inventar"),
        new("Kueche", "kueche", "Küche"),
        new("Finanzen", "finanzen", "Finanzen"),
        new("Kanaele", "kanaele", "Kanäle"),
    ];

    /// <summary>Abhängigkeitspfeile aus AD-2: Modul darf die lesende Schnittstelle des Zielmoduls aufrufen.</summary>
    public static readonly IReadOnlySet<(string Von, string Nach)> Pfeile = new HashSet<(string, string)>
    {
        ("Personen", "Konten"),
        ("Organisationen", "Konten"),
        ("Organisationen", "Personen"),
        ("Freizeiten", "Organisationen"),
        ("Freizeiten", "Personen"),
        ("Inventar", "Organisationen"),
        ("Inventar", "Freizeiten"),
        ("Sicherheit", "Freizeiten"),
        ("Sicherheit", "Personen"),
        ("Sicherheit", "Inventar"),
        ("Programm", "Freizeiten"),
        ("Programm", "Inventar"),
        ("Kueche", "Programm"),
        ("Kueche", "Freizeiten"),
        ("Kueche", "Personen"),
        ("Finanzen", "Freizeiten"),
        ("Kanaele", "Freizeiten"),
    };

    public static string Wurzel { get; } = WurzelFinden();

    public static string SpinePfad => Path.Combine(
        Wurzel, "_bmad-output", "planning-artifacts", "architecture", "architecture-Zeltlotse-2026-10-11", "ARCHITECTURE-SPINE.md");

    /// <summary>Alle gebauten Assemblies der Lösung (src) plus das Testmodul.</summary>
    public static IReadOnlyList<Assembly> Assemblies { get; } = AssembliesLaden();

    public static IEnumerable<Assembly> ModulAssemblies =>
        Module.Select(m => Assemblies.Single(a => a.GetName().Name == m.Projekt));

    public static Assembly? AssemblyVon(string projekt) =>
        Assemblies.SingleOrDefault(a => a.GetName().Name == projekt);

    private static string WurzelFinden()
    {
        var verzeichnis = new DirectoryInfo(AppContext.BaseDirectory);
        while (verzeichnis is not null && !File.Exists(Path.Combine(verzeichnis.FullName, "Zeltlotse.slnx")))
        {
            verzeichnis = verzeichnis.Parent;
        }

        return verzeichnis?.FullName ?? throw new InvalidOperationException("Zeltlotse.slnx nicht gefunden.");
    }

    private static List<Assembly> AssembliesLaden()
    {
        var namen = Directory.GetFiles(AppContext.BaseDirectory, "Zeltlotse.*.dll")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n is not null && n != typeof(Loesung).Assembly.GetName().Name)
            .Cast<string>()
            .Order();
        return namen.Select(n => Assembly.Load(n)).ToList();
    }
}

internal sealed record Modul(string Name, string Schema, string Anzeigename)
{
    public string Projekt => $"Zeltlotse.{Name}";

    public string ContractsProjekt => $"Zeltlotse.{Name}.Contracts";

    public string EventsNamensraum => $"Zeltlotse.{Name}.Contracts.Events";
}
