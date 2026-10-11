using Microsoft.EntityFrameworkCore;
using Zeltlotse.Personen.Profil;
using Zeltlotse.SharedKernel;

namespace Zeltlotse.Personen.Familien;

/// <summary>
/// Gemeinsame Schritte der Familien-Commands: Familie sicherstellen und Zeilen mit Zeilensperre (<c>FOR UPDATE</c>) in der
/// Transaktion der Pipeline laden, damit gleichzeitige Annahmen und Änderungen nacheinander laufen. Bekommt nur die Tabellen
/// des Handlers, nicht den schreibenden DbContext (AD-27). Nichts davon ist organisationseigen; kein Mandantenfilter.
/// </summary>
internal static class FamilienSperre
{
    /// <summary>
    /// Die Familie der Person; fehlt sie, entsteht sie mit der Person als erstem Elternteil (im Change-Tracker, gespeichert
    /// mit dem Command). <c>Neu</c> heißt: eben angelegt, also noch nicht in der Datenbank.
    /// </summary>
    public static async Task<(Guid FamilieId, bool Neu)> SicherstellenAsync(
        DbSet<Familie> familien, DbSet<FamilienElternteil> elternteile, Guid personId, TimeProvider uhr, CancellationToken abbruch)
    {
        var vorhanden = await elternteile.SingleOrDefaultAsync(e => e.PersonId == personId, abbruch);
        if (vorhanden is not null)
        {
            return (vorhanden.FamilieId, false);
        }

        var familie = Familie.Anlegen(Kennung.Neu(uhr), uhr.GetUtcNow());
        familien.Add(familie);
        elternteile.Add(FamilienElternteil.Anlegen(familie.Id, personId));
        return (familie.Id, true);
    }

    public static async Task<Familie?> FamilieAsync(DbSet<Familie> familien, Guid familieId, CancellationToken abbruch) =>
        (await familien
            .FromSql($"SELECT * FROM personen.familie WHERE \"Id\" = {familieId} FOR UPDATE")
            .ToListAsync(abbruch))
        .SingleOrDefault();

    public static async Task<Person?> PersonAsync(DbSet<Person> personen, Guid personId, CancellationToken abbruch) =>
        (await personen
            .FromSql($"SELECT *, xmin FROM personen.person WHERE \"Id\" = {personId} FOR UPDATE")
            .ToListAsync(abbruch))
        .SingleOrDefault();

    /// <summary>Das Kind einer Familie unter Zeilensperre; <c>null</c>, wenn es nicht (mehr) zu dieser Familie gehört.</summary>
    public static async Task<Person?> KindAsync(DbSet<Person> personen, Guid kindId, Guid familieId, CancellationToken abbruch) =>
        await PersonAsync(personen, kindId, abbruch) is { } kind && kind.FamilieId == familieId ? kind : null;

    public static async Task<FamilienEinladung?> EinladungAsync(DbSet<FamilienEinladung> einladungen, byte[] tokenHash, CancellationToken abbruch) =>
        (await einladungen
            .FromSql($"SELECT *, xmin FROM personen.familien_einladung WHERE \"TokenHash\" = {tokenHash} FOR UPDATE")
            .ToListAsync(abbruch))
        .SingleOrDefault();

    public static async Task<ProfilUebergabe?> UebergabeAsync(DbSet<ProfilUebergabe> uebergaben, byte[] tokenHash, CancellationToken abbruch) =>
        (await uebergaben
            .FromSql($"SELECT *, xmin FROM personen.profil_uebergabe WHERE \"TokenHash\" = {tokenHash} FOR UPDATE")
            .ToListAsync(abbruch))
        .SingleOrDefault();
}
