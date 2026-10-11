using Microsoft.EntityFrameworkCore;
using Zeltlotse.Konten.Domaene;

namespace Zeltlotse.Konten.Anmelden;

/// <summary>
/// Lädt eine Login-Anfrage mit Zeilensperre (<c>FOR UPDATE</c>) in der Transaktion der Pipeline: Gleichzeitige Einlösungen
/// und Code-Versuche derselben Anfrage laufen nacheinander, jeder falsche Code zählt, und nur eine Einlösung gelingt.
/// Bekommt nur die Tabelle des Handlers, nicht den schreibenden DbContext (AD-27).
/// </summary>
internal static class AnfrageSperre
{
    public static async Task<LoginAnfrage?> PerTokenHashAsync(DbSet<LoginAnfrage> anfragen, byte[] tokenHash, CancellationToken abbruch) =>
        (await anfragen
            .FromSql($"SELECT *, xmin FROM konten.login_anfrage WHERE \"TokenHash\" = {tokenHash} FOR UPDATE")
            .ToListAsync(abbruch))
        .SingleOrDefault();

    public static async Task<LoginAnfrage?> PerIdAsync(DbSet<LoginAnfrage> anfragen, Guid id, CancellationToken abbruch) =>
        (await anfragen
            .FromSql($"SELECT *, xmin FROM konten.login_anfrage WHERE \"Id\" = {id} FOR UPDATE")
            .ToListAsync(abbruch))
        .SingleOrDefault();
}
