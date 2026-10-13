using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Personen.Familien;
using Zeltlotse.Personen.Freigaben;
using Zeltlotse.Personen.Freizeitpass;
using Zeltlotse.Personen.Gaeste;
using Zeltlotse.Personen.Freizeitrollen;
using Zeltlotse.Personen.Nachweise;
using Zeltlotse.Personen.Profil;
using Zeltlotse.Personen.Schluessel;
using Zeltlotse.Personen.Zugriffsprotokoll;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Zugangslinks;

namespace Zeltlotse.Personen.Daten;

/// <summary>Schreibender DbContext; nur Command-Handler erhalten ihn (AD-27).</summary>
internal sealed class PersonenDbContext(DbContextOptions<PersonenDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulDbContext(optionen, mandantenKontext)
{
    public DbSet<PersonenSchluessel> PersonenSchluessel => Set<PersonenSchluessel>();

    public DbSet<Person> Personen => Set<Person>();

    public DbSet<FreizeitRolle> FreizeitRollen => Set<FreizeitRolle>();

    public DbSet<Familie> Familien => Set<Familie>();

    public DbSet<FamilienElternteil> Elternteile => Set<FamilienElternteil>();

    public DbSet<FamilienEinladung> FamilienEinladungen => Set<FamilienEinladung>();

    public DbSet<ProfilUebergabe> Uebergaben => Set<ProfilUebergabe>();

    public DbSet<Nachweis> Nachweise => Set<Nachweis>();

    public DbSet<NachweisEinsicht> Einsichten => Set<NachweisEinsicht>();

    public DbSet<NachweisEinsichtPosten> EinsichtPosten => Set<NachweisEinsichtPosten>();

    public DbSet<NachweisErinnerung> Erinnerungen => Set<NachweisErinnerung>();

    public DbSet<FreizeitpassErinnerung> FreizeitpassErinnerungen => Set<FreizeitpassErinnerung>();

    public DbSet<EinsichtProtokollEintrag> EinsichtProtokoll => Set<EinsichtProtokollEintrag>();

    public DbSet<GastZugang> GastZugaenge => Set<GastZugang>();

    public DbSet<Freigabe> Freigaben => Set<Freigabe>();

    public DbSet<FreizeitpassAntwort> FreizeitpassAntworten => Set<FreizeitpassAntwort>();

    protected override string Schema => PersonenModul.Schema;

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => PersonenModell.Konfigurieren(modelBuilder);
}

/// <summary>Lesender DbContext für Endpunkte: ohne Änderungsverfolgung, Speichern wirft (AD-27).</summary>
internal sealed class PersonenLeseDbContext(DbContextOptions<PersonenLeseDbContext> optionen, IMandantenKontext mandantenKontext)
    : ModulLeseDbContext(optionen, mandantenKontext)
{
    public DbSet<PersonenSchluessel> PersonenSchluessel => Set<PersonenSchluessel>();

    public DbSet<Person> Personen => Set<Person>();

    public DbSet<FreizeitRolle> FreizeitRollen => Set<FreizeitRolle>();

    public DbSet<Familie> Familien => Set<Familie>();

    public DbSet<FamilienElternteil> Elternteile => Set<FamilienElternteil>();

    public DbSet<FamilienEinladung> FamilienEinladungen => Set<FamilienEinladung>();

    public DbSet<ProfilUebergabe> Uebergaben => Set<ProfilUebergabe>();

    public DbSet<Nachweis> Nachweise => Set<Nachweis>();

    public DbSet<NachweisEinsicht> Einsichten => Set<NachweisEinsicht>();

    public DbSet<NachweisEinsichtPosten> EinsichtPosten => Set<NachweisEinsichtPosten>();

    public DbSet<NachweisErinnerung> Erinnerungen => Set<NachweisErinnerung>();

    public DbSet<FreizeitpassErinnerung> FreizeitpassErinnerungen => Set<FreizeitpassErinnerung>();

    public DbSet<EinsichtProtokollEintrag> EinsichtProtokoll => Set<EinsichtProtokollEintrag>();

    public DbSet<GastZugang> GastZugaenge => Set<GastZugang>();

    public DbSet<Freigabe> Freigaben => Set<Freigabe>();

    public DbSet<FreizeitpassAntwort> FreizeitpassAntworten => Set<FreizeitpassAntwort>();

    protected override string Schema => PersonenModul.Schema;

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder) => PersonenModell.Konfigurieren(modelBuilder);
}

/// <summary>Entitäten des Moduls; schreibender und lesender DbContext teilen diese Konfiguration.</summary>
internal static class PersonenModell
{
    public static void Konfigurieren(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PersonenSchluessel>(e =>
        {
            e.ToTable("personen_schluessel");
            e.HasKey(s => s.PersonId);
            e.Property(s => s.PersonId).ValueGeneratedNever();
            e.Property(s => s.Umhuellt).HasColumnType("bytea");
        });

        modelBuilder.Entity<Person>(e =>
        {
            e.ToTable("person");
            e.Property(p => p.Id).ValueGeneratedNever();
            e.Property(p => p.Vorname).HasMaxLength(ProfilFelder.MaximaleLaenge);
            e.Property(p => p.Nachname).HasMaxLength(ProfilFelder.MaximaleLaenge);
            e.Property(p => p.Herkunft).HasMaxLength(ProfilFelder.HerkunftMaximaleLaenge);
            // Nur bei Kinderprofilen gesetzt; Eltern lesen ihre Kinder über die Familie.
            e.HasIndex(p => p.FamilieId);
            // AD-19: Versionsnummer aus xmin, von PostgreSQL bei jeder Änderung weitergezählt.
            e.Property(p => p.Version).IsRowVersion();
        });

        // AD-8: eigene, nur über Ereignisse gepflegte Liste der Freizeit-Rollen.
        modelBuilder.Entity<FreizeitRolle>(e =>
        {
            e.ToTable("freizeit_rolle");
            e.HasKey(r => new { r.FreizeitId, r.PersonId });
            e.Property(r => r.Rolle).HasConversion<short>();
            e.HasIndex(r => r.PersonId);
        });

        modelBuilder.Entity<Familie>(e =>
        {
            e.ToTable("familie");
            e.Property(f => f.Id).ValueGeneratedNever();
        });

        // Eine Person ist Elternteil in höchstens einer Familie (eindeutiger Index auf PersonId).
        modelBuilder.Entity<FamilienElternteil>(e =>
        {
            e.ToTable("familien_elternteil");
            e.HasKey(t => new { t.FamilieId, t.PersonId });
            e.HasIndex(t => t.PersonId).IsUnique();
            e.HasOne<Familie>().WithMany().HasForeignKey(t => t.FamilieId);
        });

        modelBuilder.Entity<FamilienEinladung>(e =>
        {
            e.ToTable("familien_einladung");
            e.Property(t => t.Id).ValueGeneratedNever();
            e.Property(t => t.Mail).HasMaxLength(MailAdresse.MaximaleLaenge);
            e.Property(t => t.TokenHash).HasColumnType("bytea");
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => t.FamilieId);
            e.Property(t => t.Status).HasConversion<short>();
            e.Property(t => t.Version).IsRowVersion();
            e.HasOne<Familie>().WithMany().HasForeignKey(t => t.FamilieId);
        });

        modelBuilder.Entity<ProfilUebergabe>(e =>
        {
            e.ToTable("profil_uebergabe");
            e.Property(t => t.Id).ValueGeneratedNever();
            e.Property(t => t.Mail).HasMaxLength(MailAdresse.MaximaleLaenge);
            e.Property(t => t.TokenHash).HasColumnType("bytea");
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => t.PersonId);
            e.Property(t => t.Status).HasConversion<short>();
            e.Property(t => t.Version).IsRowVersion();
            e.HasOne<Familie>().WithMany().HasForeignKey(t => t.FamilieId);
        });

        // FR-8: je Person und Art höchstens ein Nachweis; die Datei liegt verschlüsselt im Dateispeicher (Kennung = Id).
        modelBuilder.Entity<Nachweis>(e =>
        {
            e.ToTable("nachweis");
            e.Property(n => n.Id).ValueGeneratedNever();
            e.Property(n => n.Art).HasConversion<short>();
            e.HasIndex(n => new { n.PersonId, n.Art }).IsUnique();
            e.Property(n => n.Version).IsRowVersion();
        });

        // FR-10, AD-9: Einsicht als Aggregat (organisationseigen, Mandantenfilter) mit ihren Posten.
        modelBuilder.Entity<NachweisEinsicht>(e =>
        {
            e.ToTable("nachweis_einsicht");
            e.Property(t => t.Id).ValueGeneratedNever();
            e.Property(t => t.Status).HasConversion<short>();
            e.Property(t => t.NurArten).HasColumnType("smallint[]");
            e.HasIndex(t => new { t.OrganisationId, t.PersonId });
            e.HasIndex(t => t.PersonId);
            // Je (Person, Organisation) höchstens eine offene Einsicht; offen = nicht abgeschlossen (Status 3).
            e.HasIndex(t => new { t.PersonId, t.OrganisationId })
                .IsUnique()
                .HasFilter("\"Status\" <> 3")
                .HasDatabaseName("IX_nachweis_einsicht_offen");
            e.Property(t => t.Version).IsRowVersion();
            e.HasMany(t => t.Posten).WithOne().HasForeignKey(p => p.EinsichtId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(t => t.Posten).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_posten");
        });

        modelBuilder.Entity<NachweisEinsichtPosten>(e =>
        {
            e.ToTable("nachweis_einsicht_posten");
            e.HasKey(p => new { p.EinsichtId, p.NachweisId });
            e.Property(p => p.Art).HasConversion<short>();
            e.Property(p => p.Ergebnis).HasConversion<short>();
            e.HasIndex(p => p.NachweisId);
        });

        // FR-11: die letzte Erinnerung der Leitung je (Freizeit, Person); der eindeutige Index verhindert doppelte Zeilen.
        modelBuilder.Entity<NachweisErinnerung>(e =>
        {
            e.ToTable("nachweis_erinnerung");
            e.Property(t => t.Id).ValueGeneratedNever();
            e.HasIndex(t => new { t.FreizeitId, t.PersonId }).IsUnique();
            e.HasIndex(t => t.PersonId);
        });

        // Story 2-6, FR-25: die letzte Erinnerung zum Freizeitpass je (Freizeit, Person); der eindeutige Index verhindert doppelte Zeilen.
        modelBuilder.Entity<FreizeitpassErinnerung>(e =>
        {
            e.ToTable("freizeitpass_erinnerung");
            e.Property(t => t.Id).ValueGeneratedNever();
            e.HasIndex(t => new { t.FreizeitId, t.PersonId }).IsUnique();
            e.HasIndex(t => t.PersonId);
        });

        // AD-8: Einsicht-Protokoll der betroffenen Person; höchstens ein Eintrag je (lesend, betroffen, Tag, Zweck). Der Platzhalter
        // gelöschter Personen ist ausgenommen, damit zwei gelöschte Lesende am selben Tag nicht kollidieren.
        modelBuilder.Entity<EinsichtProtokollEintrag>(e =>
        {
            e.ToTable("einsicht_protokoll");
            e.Property(t => t.Id).ValueGeneratedNever();
            e.Property(t => t.Zweck).HasConversion<short>();
            e.HasIndex(t => new { t.LesendePersonId, t.BetroffenePersonId, t.Tag, t.Zweck })
                .IsUnique()
                .HasFilter($"\"LesendePersonId\" <> '{GeloeschtePerson.Id}'")
                .HasDatabaseName("IX_einsicht_protokoll_je_tag");
            e.HasIndex(t => t.BetroffenePersonId);
        });

        // AD-11, FR-17: Gast-Link einer Gast-Familie; nur der Hash des Tokens, je Elternteil höchstens einer.
        modelBuilder.Entity<GastZugang>(e =>
        {
            e.ToTable("gast_zugang");
            e.Property(t => t.Id).ValueGeneratedNever();
            e.Property(t => t.Mail).HasMaxLength(MailAdresse.MaximaleLaenge);
            e.Property(t => t.TokenHash).HasColumnType("bytea");
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => t.ElternPersonId).IsUnique();
        });

        // AD-8: Freigabe (organisationseigen, Mandantenfilter); je Anmeldung höchstens eine.
        modelBuilder.Entity<Freigabe>(e =>
        {
            e.ToTable("freigabe");
            e.Property(t => t.Id).ValueGeneratedNever();
            e.Property(t => t.Stufe).HasConversion<short>();
            // Story 2-5: Kennungen der abgefragten Fragen; Freigaben von vor dieser Story haben keine.
            e.Property(t => t.Fragen).HasColumnType("text[]");
            e.HasIndex(t => t.AnmeldungId).IsUnique();
            e.HasIndex(t => t.PersonId);
            e.HasIndex(t => new { t.OrganisationId, t.FreizeitId });
        });

        // FR-23, AD-10, AD-14: je (Person, Kennung) eine Antwort; Wert und Sonstiges nur verschlüsselt (bytea über Geschuetzt).
        modelBuilder.Entity<FreizeitpassAntwort>(e =>
        {
            e.ToTable("freizeitpass_antwort");
            e.Property(t => t.Id).ValueGeneratedNever();
            e.Property(t => t.Kennung).HasMaxLength(50);
            e.HasIndex(t => new { t.PersonId, t.Kennung }).IsUnique();
            e.Ignore(t => t.Beantwortet);
            e.Property(t => t.Version).IsRowVersion();
        });
    }
}

/// <summary>Für <c>dotnet ef migrations add</c> (Design-Zeit).</summary>
internal sealed class PersonenDbContextFabrik : IDesignTimeDbContextFactory<PersonenDbContext>
{
    public PersonenDbContext CreateDbContext(string[] args) =>
        new(ModulRegistrierung.DesignZeitOptionen<PersonenDbContext>(PersonenModul.Schema), new MandantenKontext());
}
