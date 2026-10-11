using Microsoft.EntityFrameworkCore;
using Zeltlotse.SharedKernel.Daten;

namespace Zeltlotse.Api;

/// <summary>
/// Wendet beim Start die Migrationen aller Module in fester Reihenfolge an, Basis zuerst (Konvention „Migrationen“).
/// Bereits angewendete Migrationen bleiben unverändert.
/// </summary>
internal sealed class ModulMigrationen(IServiceProvider dienste, IEnumerable<ModulBeschreibung> module)
{
    public static readonly string[] Reihenfolge =
        ["Konten", "Personen", "Organisationen", "Freizeiten", "Inventar", "Sicherheit", "Programm", "Kueche", "Finanzen", "Kanaele"];

    private readonly List<string> _angewendet = [];

    /// <summary>Die Module in der Reihenfolge, in der ihre Migrationen zuletzt angewendet wurden.</summary>
    public IReadOnlyList<string> AngewendeteReihenfolge => _angewendet;

    /// <summary>Ordnet die angemeldeten Module nach <see cref="Reihenfolge"/>; ein unbekanntes Modul ist ein Fehler.</summary>
    public static IReadOnlyList<ModulBeschreibung> Ordnen(IEnumerable<ModulBeschreibung> module)
    {
        var liste = module.ToList();
        var unbekannt = liste.Where(m => !Reihenfolge.Contains(m.Name)).Select(m => m.Name).ToList();
        if (unbekannt.Count > 0)
        {
            throw new InvalidOperationException($"Module ohne Platz in der Migrationsreihenfolge: {string.Join(", ", unbekannt)}");
        }

        return [.. liste.OrderBy(m => Array.IndexOf(Reihenfolge, m.Name))];
    }

    public async Task AnwendenAsync(CancellationToken abbruch = default)
    {
        _angewendet.Clear();
        await using var scope = dienste.CreateAsyncScope();
        foreach (var modul in Ordnen(module))
        {
            var db = (DbContext)scope.ServiceProvider.GetRequiredService(modul.DbContextTyp);
            await db.Database.MigrateAsync(abbruch);
            _angewendet.Add(modul.Name);
        }
    }
}
