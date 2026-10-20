using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Zeltlotse.Api.Contracts.Kanaele;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Push;

namespace Zeltlotse.Kanaele.Daten;

using Nachricht = Zeltlotse.Kanaele.Nachrichten.Nachricht;
using Notiz = Zeltlotse.Kanaele.Notizen.Notiz;
using PushAbo = Zeltlotse.Kanaele.Push.PushAbo;

/// <summary>Schreibender DbContext; nur Command-Handler erhalten ihn (AD-27).</summary>
internal sealed class KanaeleDbContext(DbContextOptions<KanaeleDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulDbContext(optionen, mandantenKontext)
{
    /// <summary>Story 6-1: die Nachrichten der Kanäle „Team“ und „Alle“.</summary>
    public DbSet<Nachricht> Nachrichten => Set<Nachricht>();

    /// <summary>Story 6-1: die Push-Abos der Browser, höchstens eines je (Freizeit, Endpunkt).</summary>
    public DbSet<PushAbo> PushAbos => Set<PushAbo>();

    /// <summary>Story 6-3: die Punkte von Ansagen-Zettel und Besprechungsliste.</summary>
    public DbSet<Notiz> Notizen => Set<Notiz>();

    protected override string Schema => KanaeleModul.Schema;

    /// <summary>
    /// Story 6-1: Speichert das Push-Abo des Browsers (<c>INSERT … ON CONFLICT DO UPDATE</c> auf den eindeutigen Index je Freizeit und Endpunkt): Ein
    /// erneutes Senden, auch von einer anderen Person am selben Gerät, überschreibt Person, Schlüssel und Zeitpunkt, statt das Abo zu verdoppeln. Zwei
    /// gleichzeitige Anfragen ergeben so genau ein Abo, ohne Fehler.
    /// </summary>
    public Task AboSpeichernAsync(
        Guid id, Guid organisationId, Guid freizeitId, Guid personId, string endpunkt, string p256dh, string auth, DateTimeOffset jetzt, CancellationToken abbruch) =>
        Database.ExecuteSqlAsync(
            $"""
             INSERT INTO kanaele.push_abo ("Id", "OrganisationId", "FreizeitId", "PersonId", "Endpunkt", "P256dh", "Auth", "AngelegtAm")
             VALUES ({id}, {organisationId}, {freizeitId}, {personId}, {endpunkt}, {p256dh}, {auth}, {jetzt})
             ON CONFLICT ("FreizeitId", "Endpunkt") DO UPDATE
             SET "PersonId" = EXCLUDED."PersonId", "P256dh" = EXCLUDED."P256dh", "Auth" = EXCLUDED."Auth", "AngelegtAm" = EXCLUDED."AngelegtAm"
             WHERE kanaele.push_abo."OrganisationId" = EXCLUDED."OrganisationId"
             """,
            abbruch);

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => KanaeleModell.Konfigurieren(modelBuilder);
}

/// <summary>Lesender DbContext für Endpunkte: ohne Änderungsverfolgung, Speichern wirft (AD-27).</summary>
internal sealed class KanaeleLeseDbContext(DbContextOptions<KanaeleLeseDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulLeseDbContext(optionen, mandantenKontext)
{
    public DbSet<Nachricht> Nachrichten => Set<Nachricht>();

    public DbSet<PushAbo> PushAbos => Set<PushAbo>();

    public DbSet<Notiz> Notizen => Set<Notiz>();

    protected override string Schema => KanaeleModul.Schema;

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => KanaeleModell.Konfigurieren(modelBuilder);
}

/// <summary>Entitäten des Moduls; schreibender und lesender DbContext teilen diese Konfiguration.</summary>
internal static class KanaeleModell
{
    /// <summary>Längste Schlüssel des Browsers (base64url; p256dh hat 87, auth 22 Zeichen).</summary>
    public const int SchluesselMaximaleLaenge = 200;

    public static void Konfigurieren(ModelBuilder modelBuilder)
    {
        // Story 6-1, FR-61: Nachrichten (Arbeitsdaten der Freizeit, AD-16), gelesen je Kanal nach Zeitpunkt.
        modelBuilder.Entity<Nachricht>(e =>
        {
            e.ToTable("nachricht");
            e.Property(n => n.Id).ValueGeneratedNever();
            e.Property(n => n.Text).HasMaxLength(KanalFelder.TextMaximaleLaenge);
            e.Property(n => n.Laufnummer).UseIdentityByDefaultColumn();
            e.HasIndex(n => new { n.FreizeitId, n.Kanal, n.GesendetAm });
            e.HasIndex(n => n.OrganisationId);
            e.HasIndex(n => n.AutorPersonId);
        });

        // Story 6-1, AD-21: Push-Abos (personeneigen, AD-16), höchstens eines je Freizeit und Endpunkt.
        modelBuilder.Entity<PushAbo>(e =>
        {
            e.ToTable("push_abo");
            e.Property(a => a.Id).ValueGeneratedNever();
            e.Property(a => a.Endpunkt).HasMaxLength(PushEndpunkte.MaximaleLaenge);
            e.Property(a => a.P256dh).HasMaxLength(SchluesselMaximaleLaenge);
            e.Property(a => a.Auth).HasMaxLength(SchluesselMaximaleLaenge);
            e.HasIndex(a => new { a.FreizeitId, a.Endpunkt }).IsUnique();
            e.HasIndex(a => a.OrganisationId);
            e.HasIndex(a => a.PersonId);
        });

        // Story 6-3, FR-63: Ansagen-Zettel und Besprechungspunkte (Arbeitsdaten der Freizeit ohne Personenbezug, AD-16), gelesen je Liste.
        modelBuilder.Entity<Notiz>(e =>
        {
            e.ToTable("notiz");
            e.Property(n => n.Id).ValueGeneratedNever();
            e.Property(n => n.Text).HasMaxLength(KanalFelder.NotizTextMaximaleLaenge);
            e.Property(n => n.Laufnummer).UseIdentityByDefaultColumn();
            e.HasIndex(n => new { n.FreizeitId, n.Art });
            e.HasIndex(n => n.OrganisationId);
        });
    }
}

/// <summary>Für <c>dotnet ef migrations add</c> (Design-Zeit).</summary>
internal sealed class KanaeleDbContextFabrik : IDesignTimeDbContextFactory<KanaeleDbContext>
{
    public KanaeleDbContext CreateDbContext(string[] args) =>
        new(ModulRegistrierung.DesignZeitOptionen<KanaeleDbContext>(KanaeleModul.Schema), new MandantenKontext());
}
