using Microsoft.EntityFrameworkCore;

namespace Zeltlotse.SharedKernel.Outbox;

/// <summary>Ein Ereignis in der Outbox des auslösenden Moduls, mit den Feldern des Umschlags (AD-4).</summary>
internal sealed class OutboxNachricht
{
    public required Guid Id { get; init; }

    public required string Typ { get; init; }

    public required string Inhalt { get; init; }

    public required DateTimeOffset Zeitpunkt { get; init; }

    public Guid? OrganisationId { get; init; }

    public required Guid DatensatzId { get; init; }

    public required long Folgenummer { get; init; }
}

/// <summary>Zustellstand je (Ereignis, Empfänger) im Schema des auslösenden Moduls.</summary>
internal sealed class Zustellung
{
    public required Guid EreignisId { get; init; }

    public required string Empfaenger { get; init; }

    public int Versuche { get; set; }

    public DateTimeOffset NaechsterVersuch { get; set; }

    public DateTimeOffset? ErledigtAm { get; set; }

    /// <summary>Nur der Ausnahme-Typ, nie eine Meldung mit möglichen Personendaten (AD-22).</summary>
    public string? LetzterFehler { get; set; }
}

/// <summary>Verarbeitetes Paar (Ereignis, Empfänger) im Schema des empfangenden Moduls.</summary>
internal sealed class InboxEintrag
{
    public required Guid EreignisId { get; init; }

    public required string Empfaenger { get; init; }

    public required DateTimeOffset VerarbeitetAm { get; init; }
}

/// <summary>Höchste verarbeitete Folgenummer je (Datensatz, Empfänger).</summary>
internal sealed class InboxStand
{
    public required Guid DatensatzId { get; init; }

    public required string Empfaenger { get; init; }

    public long Folgenummer { get; set; }
}

internal static class OutboxModell
{
    public static void Konfigurieren(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxNachricht>(e =>
        {
            e.ToTable("outbox");
            e.HasKey(n => n.Id);
            e.Property(n => n.Typ).HasMaxLength(300);
            e.Property(n => n.Inhalt).HasColumnType("jsonb");
            e.HasIndex(n => new { n.DatensatzId, n.Folgenummer });
        });

        modelBuilder.Entity<Zustellung>(e =>
        {
            e.ToTable("outbox_zustellung");
            e.HasKey(z => new { z.EreignisId, z.Empfaenger });
            e.Property(z => z.Empfaenger).HasMaxLength(300);
            e.Property(z => z.LetzterFehler).HasMaxLength(200);
            e.HasIndex(z => new { z.ErledigtAm, z.NaechsterVersuch });
            e.HasOne<OutboxNachricht>().WithMany().HasForeignKey(z => z.EreignisId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InboxEintrag>(e =>
        {
            e.ToTable("inbox");
            e.HasKey(i => new { i.EreignisId, i.Empfaenger });
            e.Property(i => i.Empfaenger).HasMaxLength(300);
        });

        modelBuilder.Entity<InboxStand>(e =>
        {
            e.ToTable("inbox_stand");
            e.HasKey(i => new { i.DatensatzId, i.Empfaenger });
            e.Property(i => i.Empfaenger).HasMaxLength(300);
        });
    }
}
