using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zeltlotse.SharedKernel.Datenschutz;

namespace Zeltlotse.ArchitectureTests.Negativ;

// Negativ-Fixtures für AD-14 und AD-22. Werden nie ausgeführt, nur von den Architekturtests als Verstoß erwartet.

/// <summary>Verstoß AD-14: <c>[Besonders]</c> an einer Klartext-Eigenschaft.</summary>
internal sealed class BesondersAlsString
{
    [Besonders]
    public string Allergie { get; set; } = "";
}

/// <summary>Verstoß AD-14: Chiffretext ohne Kennzeichnung.</summary>
internal sealed class GeschuetztOhneBesonders
{
    public Geschuetzt? Notiz { get; set; }
}

/// <summary>Verstoß AD-22: Logging an <c>[LoggerMessage]</c> vorbei.</summary>
internal static class DirektLogger
{
    public static void Melden(ILogger logger, string name) => logger.LogInformation("Angemeldet: {Name}", name);
}

/// <summary>Erlaubt: quellgeneriertes Logging.</summary>
internal static partial class QuellgenerierterLogger
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Angemeldet: {PersonId}")]
    public static partial void Angemeldet(ILogger logger, Guid personId);
}

/// <summary>Verstoß AD-22: Parameterwerte in EF-Protokollen.</summary>
internal static class SensiblesProtokoll
{
    public static DbContextOptionsBuilder Konfigurieren(DbContextOptionsBuilder optionen) => optionen.EnableSensitiveDataLogging();
}

/// <summary>Verstoß AD-22: Fehlerdetails der Datenbank.</summary>
internal static class DetaillierteFehler
{
    public static DbContextOptionsBuilder Konfigurieren(DbContextOptionsBuilder optionen) => optionen.EnableDetailedErrors();
}

/// <summary>Verstoß AD-3/AD-22: ein Betriebs-Kontext, der eine Tabelle im Schema eines Moduls anlegt.</summary>
internal sealed class NegativBetriebDbContext : DbContext
{
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseNpgsql("Host=nirgends;Database=keine", n => n.MigrationsHistoryTable("__EFMigrationsHistory", "betrieb"));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("betrieb");
        modelBuilder.Entity<FremdeZeile>().ToTable("fremd", "personen");
    }

    internal sealed class FremdeZeile
    {
        public Guid Id { get; set; }
    }
}
