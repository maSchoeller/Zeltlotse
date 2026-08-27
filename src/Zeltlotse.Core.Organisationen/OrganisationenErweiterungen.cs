using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Core.Konten.Contracts;
using Zeltlotse.Core.Organisationen.Contracts;
using Zeltlotse.Core.Persistenz;

namespace Zeltlotse.Core.Organisationen;

public static class OrganisationenErweiterungen
{
    public static IServiceCollection AddOrganisationen(this IServiceCollection dienste)
    {
        return dienste;
    }

    public static IEndpointRouteBuilder MapOrganisationen(this IEndpointRouteBuilder route)
    {
        var verwaltung = route.MapGroup("/api/verwaltung/organisationen").RequireAuthorization();

        verwaltung.MapGet("/", ListeAsync);
        verwaltung.MapPost("/", AnlegenAsync);
        verwaltung.MapGet("/slugvorschau", VorschauAsync);
        verwaltung.MapPost("/{id:guid}/leitung", LeitungEinladenAsync);
        verwaltung.MapPost("/{id:guid}/loeschung-ausfuehren", LoeschungAusfuehrenAsync);

        var organisation = route.MapGroup("/api/o/{slug}").RequireAuthorization();

        organisation.MapGet("/", EigeneAsync);
        organisation.MapGet("/mitglieder", MitgliederAsync);
        organisation.MapGet("/einladungen", EinladungenAsync);
        organisation.MapPost("/einladungen", EinladenAsync);
        organisation.MapPost("/einladungen/{id:guid}/erneuern", EinladungErneuernAsync);
        organisation.MapPost("/loeschantrag", LoeschantragAsync);
        organisation.MapDelete("/loeschantrag", LoeschantragZuruecknehmenAsync);

        return route;
    }

    // ---------- Betreiber ----------

    private static async Task<IResult> ListeAsync(
        ClaimsPrincipal angemeldet, ZeltlotseDbContext datenbank, CancellationToken abbruch)
    {
        if (!angemeldet.IstGlobalAdmin())
        {
            return Results.Forbid();
        }

        var organisationen = await datenbank.Organisationen
            .Aktiv()
            .OrderBy(o => o.Name)
            .Select(o => new OrganisationVerwaltungDto(
                o.Id,
                o.Name,
                o.Slug,
                o.Mitgliedschaften.Count,
                o.Mitgliedschaften
                    .Where(m => m.Rolle == OrgRolle.OrgAdmin)
                    .Select(m => m.Nutzer!.Email)
                    .FirstOrDefault(),
                o.LoeschungBeantragtAm,
                o.GeloeschtAm))
            .ToListAsync(abbruch);

        return Results.Ok(organisationen);
    }

    private static IResult VorschauAsync(string name)
        => Results.Ok(new NamensvorschlagDto(Slug.AusName(name ?? string.Empty)));

    private static async Task<IResult> AnlegenAsync(
        OrganisationAnlegen anfrage,
        ClaimsPrincipal angemeldet,
        ZeltlotseDbContext datenbank,
        CancellationToken abbruch)
    {
        if (!angemeldet.IstGlobalAdmin())
        {
            return Results.Forbid();
        }

        var name = (anfrage.Name ?? string.Empty).Trim();

        if (name.Length is 0 or > 200)
        {
            return Results.BadRequest(new { fehler = "Der Name darf nicht leer sein." });
        }

        var belegt = await datenbank.Organisationen
            .IgnoreQueryFilters()
            .Select(o => o.Slug)
            .ToListAsync(abbruch);

        var organisation = new Organisation
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = Slug.Eindeutig(name, belegt.Contains),
            ErstelltAm = DateTimeOffset.UtcNow,
        };

        datenbank.Organisationen.Add(organisation);
        await datenbank.SaveChangesAsync(abbruch);

        return Results.Created($"/api/o/{organisation.Slug}", new OrganisationVerwaltungDto(
            organisation.Id, organisation.Name, organisation.Slug, 0, null, null, null));
    }

    private static async Task<IResult> LeitungEinladenAsync(
        Guid id,
        EinladungAnlegen anfrage,
        ClaimsPrincipal angemeldet,
        ZeltlotseDbContext datenbank,
        IEinladungen einladungen,
        CancellationToken abbruch)
    {
        if (!angemeldet.IstGlobalAdmin())
        {
            return Results.Forbid();
        }

        if (!await datenbank.Organisationen.Aktiv().AnyAsync(o => o.Id == id, abbruch))
        {
            return Results.NotFound();
        }

        var erzeugt = await einladungen.ErstellenAsync(
            id,
            angemeldet.NutzerId(),
            anfrage with { Ziel = Einladungsziel.Organisation, OrgRolle = OrgRolle.OrgAdmin },
            abbruch);

        return Results.Ok(erzeugt);
    }

    /// <summary>
    /// Führt eine Löschung aus, die die Organisationsleitung beantragt hat.
    /// Ohne Antrag geschieht nichts — der Betreiber löscht keinen Träger im
    /// Alleingang.
    /// </summary>
    private static async Task<IResult> LoeschungAusfuehrenAsync(
        Guid id,
        ClaimsPrincipal angemeldet,
        ZeltlotseDbContext datenbank,
        CancellationToken abbruch)
    {
        if (!angemeldet.IstGlobalAdmin())
        {
            return Results.Forbid();
        }

        var organisation = await datenbank.Organisationen
            .Aktiv()
            .FirstOrDefaultAsync(o => o.Id == id, abbruch);

        if (organisation is null)
        {
            return Results.NotFound();
        }

        if (organisation.LoeschungBeantragtAm is null)
        {
            return Results.Conflict(new
            {
                fehler = "Für diese Organisation liegt kein Löschantrag vor.",
            });
        }

        organisation.GeloeschtAm = DateTimeOffset.UtcNow;
        await datenbank.SaveChangesAsync(abbruch);

        return Results.NoContent();
    }

    // ---------- Organisation ----------

    private static async Task<IResult> EigeneAsync(
        string slug, Organisationsaufloeser aufloeser, CancellationToken abbruch)
    {
        var treffer = await aufloeser.AufloesenAsync(slug, abbruch);

        return treffer is null
            ? Organisationsaufloeser.KeinZugriff
            : Results.Ok(new OrganisationDto(
                treffer.Organisation.Id,
                treffer.Organisation.Name,
                treffer.Organisation.Slug,
                treffer.Rechte.Rolle,
                treffer.Organisation.LoeschungBeantragtAm));
    }

    private static async Task<IResult> MitgliederAsync(
        string slug,
        Organisationsaufloeser aufloeser,
        ZeltlotseDbContext datenbank,
        CancellationToken abbruch)
    {
        var treffer = await aufloeser.AufloesenAsync(slug, abbruch);

        if (treffer is null)
        {
            return Organisationsaufloeser.KeinZugriff;
        }

        if (!treffer.Rechte.DarfVerwalten)
        {
            return Organisationsaufloeser.KeineBerechtigung;
        }

        var mitglieder = await datenbank.OrgMitgliedschaften
            .Where(m => m.OrganisationId == treffer.Organisation.Id)
            .OrderBy(m => m.Nutzer!.Name).ThenBy(m => m.Nutzer!.Email)
            .Select(m => new MitgliedDto(
                m.NutzerId, m.Nutzer!.Name, m.Nutzer.Email ?? string.Empty, m.Rolle, m.SeitAm))
            .ToListAsync(abbruch);

        return Results.Ok(mitglieder);
    }

    private static async Task<IResult> EinladungenAsync(
        string slug,
        Organisationsaufloeser aufloeser,
        IEinladungen einladungen,
        CancellationToken abbruch)
    {
        var treffer = await aufloeser.AufloesenAsync(slug, abbruch);

        if (treffer is null)
        {
            return Organisationsaufloeser.KeinZugriff;
        }

        if (!treffer.Rechte.DarfVerwalten)
        {
            return Organisationsaufloeser.KeineBerechtigung;
        }

        return Results.Ok(await einladungen.OffeneAsync(treffer.Organisation.Id, abbruch));
    }

    private static async Task<IResult> EinladenAsync(
        string slug,
        EinladungAnlegen anfrage,
        ClaimsPrincipal angemeldet,
        Organisationsaufloeser aufloeser,
        IEinladungen einladungen,
        CancellationToken abbruch)
    {
        var treffer = await aufloeser.AufloesenAsync(slug, abbruch);

        if (treffer is null)
        {
            return Organisationsaufloeser.KeinZugriff;
        }

        if (!treffer.Rechte.DarfVerwalten)
        {
            return Organisationsaufloeser.KeineBerechtigung;
        }

        // Der OrgAdmin kann keine weiteren OrgAdmins ernennen — das bleibt
        // beim Betreiber, so wie die Delegationskette es vorsieht.
        var bereinigt = anfrage with
        {
            Ziel = Einladungsziel.Organisation,
            OrgRolle = OrgRolle.Mitglied,
        };

        return Results.Ok(await einladungen.ErstellenAsync(
            treffer.Organisation.Id, angemeldet.NutzerId(), bereinigt, abbruch));
    }

    /// <summary>
    /// Der Klartext eines Einladungstokens existiert genau einmal — ein
    /// verlorener Link lässt sich nicht wieder anzeigen. Statt ihn zu lagern,
    /// wird die alte Einladung entwertet und eine neue ausgegeben.
    /// </summary>
    private static async Task<IResult> EinladungErneuernAsync(
        string slug,
        Guid id,
        ClaimsPrincipal angemeldet,
        Organisationsaufloeser aufloeser,
        ZeltlotseDbContext datenbank,
        IEinladungen einladungen,
        CancellationToken abbruch)
    {
        var treffer = await aufloeser.AufloesenAsync(slug, abbruch);

        if (treffer is null)
        {
            return Organisationsaufloeser.KeinZugriff;
        }

        if (!treffer.Rechte.DarfVerwalten)
        {
            return Organisationsaufloeser.KeineBerechtigung;
        }

        var alte = await datenbank.Einladungen
            .FirstOrDefaultAsync(e => e.Id == id && e.EingeloestAm == null, abbruch);

        if (alte is null)
        {
            return Results.NotFound();
        }

        var vorlage = new EinladungAnlegen(
            alte.Name, alte.EMail, alte.Ziel, alte.OrgRolle, alte.FreizeitRolle, alte.FreizeitId);

        datenbank.Einladungen.Remove(alte);
        await datenbank.SaveChangesAsync(abbruch);

        return Results.Ok(await einladungen.ErstellenAsync(
            treffer.Organisation.Id, angemeldet.NutzerId(), vorlage, abbruch));
    }

    private static async Task<IResult> LoeschantragAsync(
        string slug,
        Organisationsaufloeser aufloeser,
        ZeltlotseDbContext datenbank,
        CancellationToken abbruch)
    {
        var treffer = await aufloeser.AufloesenAsync(slug, abbruch);

        if (treffer is null)
        {
            return Organisationsaufloeser.KeinZugriff;
        }

        if (!treffer.Rechte.DarfVerwalten)
        {
            return Organisationsaufloeser.KeineBerechtigung;
        }

        treffer.Organisation.LoeschungBeantragtAm ??= DateTimeOffset.UtcNow;
        await datenbank.SaveChangesAsync(abbruch);

        return Results.NoContent();
    }

    private static async Task<IResult> LoeschantragZuruecknehmenAsync(
        string slug,
        Organisationsaufloeser aufloeser,
        ZeltlotseDbContext datenbank,
        CancellationToken abbruch)
    {
        var treffer = await aufloeser.AufloesenAsync(slug, abbruch);

        if (treffer is null)
        {
            return Organisationsaufloeser.KeinZugriff;
        }

        if (!treffer.Rechte.DarfVerwalten)
        {
            return Organisationsaufloeser.KeineBerechtigung;
        }

        treffer.Organisation.LoeschungBeantragtAm = null;
        await datenbank.SaveChangesAsync(abbruch);

        return Results.NoContent();
    }
}
