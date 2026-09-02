using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Core.Freizeiten.Contracts;
using Zeltlotse.Core.Konten.Contracts;
using Zeltlotse.Core.Organisationen.Contracts;
using Zeltlotse.Core.Persistenz;

namespace Zeltlotse.Server;

public static class Startvorgang
{
    /// <summary>
    /// Nur die Migration, ohne Kestrel zu starten — der Weg für den
    /// Migrations-Job im Deploy-Workflow. Läuft einmalig, bevor die neue
    /// Server-Revision Datenverkehr bekommt; siehe design.md des Laufs
    /// 2026-09-02-azure-produktivbetrieb.
    /// </summary>
    public static async Task MigrierenAsync(WebApplication app)
    {
        using var bereich = app.Services.CreateScope();

        var mandant = bereich.ServiceProvider.GetRequiredService<MandantKontext>();
        mandant.Wartung = true;

        var datenbank = bereich.ServiceProvider.GetRequiredService<ZeltlotseDbContext>();
        await datenbank.Database.MigrateAsync();
    }

    /// <summary>
    /// Außerhalb der Produktion (Entwicklung, Tests) migriert eine einzelne
    /// Instanz sich selbst beim Hochfahren — bequem und ohne Wettlauf-Risiko,
    /// weil dort nie mehr als eine Instanz gleichzeitig startet. Produktiv
    /// migriert stattdessen ausschließlich <see cref="MigrierenAsync"/> als
    /// eigener Deploy-Schritt (siehe dort).
    /// </summary>
    public static async Task VorbereitenAsync(WebApplication app)
    {
        using var bereich = app.Services.CreateScope();

        var mandant = bereich.ServiceProvider.GetRequiredService<MandantKontext>();
        mandant.Wartung = true;

        if (!app.Environment.IsProduction())
        {
            var datenbank = bereich.ServiceProvider.GetRequiredService<ZeltlotseDbContext>();
            await datenbank.Database.MigrateAsync();
        }

        if (app.Environment.IsDevelopment())
        {
            await EntwicklungsdatenAsync(bereich.ServiceProvider);
        }
    }

    /// <summary>
    /// Die Konten aus docs/local-testing.md. Nur in der Entwicklung — produktiv
    /// führt der Weg ausschließlich über die Einrichtungsseite.
    /// </summary>
    private static async Task EntwicklungsdatenAsync(IServiceProvider dienste)
    {
        const string kennwort = "Entwicklung!1";

        var datenbank = dienste.GetRequiredService<ZeltlotseDbContext>();
        var nutzerverwaltung = dienste.GetRequiredService<UserManager<Nutzer>>();

        if (await datenbank.Users.AnyAsync())
        {
            return;
        }

        var betreiber = await AnlegenAsync(
            nutzerverwaltung, "Bea Betreiber", "betreiber@zeltlotse.local", kennwort, true);
        var leitung = await AnlegenAsync(
            nutzerverwaltung, "Lena Leitner", "leitung@zeltlotse.local", kennwort, false);
        var freizeitleitung = await AnlegenAsync(
            nutzerverwaltung, "Frieder Falk", "freizeit@zeltlotse.local", kennwort, false);
        var team = await AnlegenAsync(
            nutzerverwaltung, "Timo Teichmann", "team@zeltlotse.local", kennwort, false);

        var organisation = new Organisation
        {
            Id = Guid.NewGuid(),
            Name = "Ev. Kirchengemeinde Musterstadt",
            Slug = "ev-kirchengemeinde-musterstadt",
            ErstelltAm = DateTimeOffset.UtcNow,
        };

        var zweite = new Organisation
        {
            Id = Guid.NewGuid(),
            Name = "Bezirksjugendwerk Beispieltal",
            Slug = "bezirksjugendwerk-beispieltal",
            ErstelltAm = DateTimeOffset.UtcNow,
        };

        datenbank.Organisationen.AddRange(organisation, zweite);

        datenbank.OrgMitgliedschaften.AddRange(
            Mitglied(leitung, organisation, OrgRolle.OrgAdmin),
            Mitglied(freizeitleitung, organisation, OrgRolle.Mitglied),
            Mitglied(team, organisation, OrgRolle.Mitglied),
            Mitglied(freizeitleitung, zweite, OrgRolle.Mitglied));

        var sommer = new Freizeit
        {
            Id = Guid.NewGuid(),
            TenantId = organisation.Id,
            Name = "Sommerfreizeit 2027",
            Beginn = new DateOnly(2027, 7, 26),
            Ende = new DateOnly(2027, 8, 9),
            Ort = "Waldheim am See",
            Status = FreizeitStatus.Offen,
            ErstelltAm = DateTimeOffset.UtcNow,
        };

        var konfi = new Freizeit
        {
            Id = Guid.NewGuid(),
            TenantId = zweite.Id,
            Name = "Konfi-Wochenende",
            Status = FreizeitStatus.Offen,
            ErstelltAm = DateTimeOffset.UtcNow,
        };

        datenbank.Freizeiten.AddRange(sommer, konfi);

        datenbank.FreizeitZuordnungen.AddRange(
            new FreizeitZuordnung
            {
                NutzerId = freizeitleitung.Id,
                FreizeitId = sommer.Id,
                TenantId = organisation.Id,
                Rolle = FreizeitRolle.Leitung,
            },
            new FreizeitZuordnung
            {
                NutzerId = team.Id,
                FreizeitId = sommer.Id,
                TenantId = organisation.Id,
                Rolle = FreizeitRolle.Mitarbeiter,
            },
            new FreizeitZuordnung
            {
                NutzerId = freizeitleitung.Id,
                FreizeitId = konfi.Id,
                TenantId = zweite.Id,
                Rolle = FreizeitRolle.Mitarbeiter,
            });

        await datenbank.SaveChangesAsync();

        _ = betreiber;
    }

    private static OrgMitgliedschaft Mitglied(Nutzer nutzer, Organisation organisation, OrgRolle rolle)
        => new()
        {
            NutzerId = nutzer.Id,
            OrganisationId = organisation.Id,
            Rolle = rolle,
            SeitAm = DateTimeOffset.UtcNow,
        };

    private static async Task<Nutzer> AnlegenAsync(
        UserManager<Nutzer> verwaltung, string name, string email, string kennwort, bool globalAdmin)
    {
        var nutzer = new Nutzer
        {
            Id = Guid.NewGuid(),
            Name = name,
            UserName = email,
            Email = email,
            IstGlobalAdmin = globalAdmin,
            ErstelltAm = DateTimeOffset.UtcNow,
        };

        var ergebnis = await verwaltung.CreateAsync(nutzer, kennwort);

        if (!ergebnis.Succeeded)
        {
            throw new InvalidOperationException(
                $"Entwicklungskonto {email}: {string.Join(" ", ergebnis.Errors.Select(f => f.Description))}");
        }

        return nutzer;
    }
}
