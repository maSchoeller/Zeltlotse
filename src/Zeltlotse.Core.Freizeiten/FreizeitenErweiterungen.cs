using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Core.Freizeiten.Contracts;
using Zeltlotse.Core.Konten.Contracts;
using Zeltlotse.Core.Organisationen.Contracts;
using Zeltlotse.Core.Persistenz;

namespace Zeltlotse.Core.Freizeiten;

public static class FreizeitenErweiterungen
{
    /// <summary>Aufbewahrungsfrist des Papierkorbs.</summary>
    public const int PapierkorbTage = 30;

    public static IServiceCollection AddFreizeiten(this IServiceCollection dienste) => dienste;

    public static IEndpointRouteBuilder MapFreizeiten(this IEndpointRouteBuilder route)
    {
        route.MapGet("/api/freizeiten/meine", MeineAsync).RequireAuthorization();

        var gruppe = route.MapGroup("/api/o/{slug}").RequireAuthorization();

        gruppe.MapGet("/freizeiten", ListeAsync);
        gruppe.MapPost("/freizeiten", AnlegenAsync);
        gruppe.MapGet("/freizeiten/{id:guid}", EinzelnAsync);
        gruppe.MapPut("/freizeiten/{id:guid}", AendernAsync);
        gruppe.MapDelete("/freizeiten/{id:guid}", LoeschenAsync);
        gruppe.MapGet("/freizeiten/{id:guid}/team", TeamAsync);
        gruppe.MapPost("/freizeiten/{id:guid}/team", TeamHinzufuegenAsync);
        gruppe.MapDelete("/freizeiten/{id:guid}/team/{nutzerId:guid}", TeamEntfernenAsync);
        gruppe.MapPost("/freizeiten/{id:guid}/einladungen", TeamEinladenAsync);
        gruppe.MapGet("/papierkorb", PapierkorbAsync);
        gruppe.MapPost("/papierkorb/{id:guid}/wiederherstellen", WiederherstellenAsync);

        return route;
    }

    /// <summary>
    /// Die Startseite. Bewusst ohne Mandant in der Adresse: Sie fragt über alle
    /// Zuordnungen des Nutzers hinweg. Die Row-Level-Security begrenzt das auf
    /// seine Organisationen, der EF-Filter bleibt hier offen.
    /// </summary>
    private static async Task<IResult> MeineAsync(
        ClaimsPrincipal angemeldet, ZeltlotseDbContext datenbank, CancellationToken abbruch)
    {
        var nutzerId = angemeldet.NutzerId();

        var verwaltete = await datenbank.OrgMitgliedschaften
            .Where(m => m.NutzerId == nutzerId && m.Rolle == OrgRolle.OrgAdmin)
            .Select(m => m.OrganisationId)
            .ToListAsync(abbruch);

        var freizeiten = await datenbank.Freizeiten
            .Aktiv()
            // Beide Navigationen werden für die Abbildung gebraucht; ohne sie
            // hängt das Ergebnis davon ab, was zufällig schon geladen ist.
            .Include(f => f.Organisation)
            .Include(f => f.Zuordnungen)
            .Where(f => f.Organisation!.GeloeschtAm == null
                && (f.Zuordnungen.Any(z => z.NutzerId == nutzerId)
                    || verwaltete.Contains(f.TenantId)))
            .OrderBy(f => f.Beginn == null ? 0 : 1)
            .ThenByDescending(f => f.Beginn)
            .ThenBy(f => f.Name)
            .Select(f => Abbilden(f, nutzerId, verwaltete.Contains(f.TenantId)))
            .ToListAsync(abbruch);

        return Results.Ok(freizeiten);
    }

    private static async Task<IResult> ListeAsync(
        string slug,
        ClaimsPrincipal angemeldet,
        Organisationsaufloeser aufloeser,
        ZeltlotseDbContext datenbank,
        CancellationToken abbruch)
    {
        var treffer = await aufloeser.AufloesenAsync(slug, abbruch);

        if (treffer is null)
        {
            return Organisationsaufloeser.KeinZugriff;
        }

        var nutzerId = angemeldet.NutzerId();
        var darfVerwalten = treffer.Rechte.DarfVerwalten;

        var freizeiten = await datenbank.Freizeiten
            .Aktiv()
            .Include(f => f.Organisation)
            .Include(f => f.Zuordnungen)
            .Where(f => darfVerwalten || f.Zuordnungen.Any(z => z.NutzerId == nutzerId))
            .OrderBy(f => f.Status)
            .ThenBy(f => f.Beginn == null ? 0 : 1)
            .ThenByDescending(f => f.Beginn)
            .ThenBy(f => f.Name)
            .Select(f => Abbilden(f, nutzerId, darfVerwalten))
            .ToListAsync(abbruch);

        return Results.Ok(freizeiten);
    }

    private static async Task<IResult> AnlegenAsync(
        string slug,
        FreizeitAnlegen anfrage,
        ClaimsPrincipal angemeldet,
        Organisationsaufloeser aufloeser,
        ZeltlotseDbContext datenbank,
        CancellationToken abbruch)
    {
        var treffer = await aufloeser.AufloesenAsync(slug, abbruch);

        if (treffer is null || !treffer.Rechte.DarfVerwalten)
        {
            return Organisationsaufloeser.KeinZugriff;
        }

        if (Pruefen(anfrage.Name, anfrage.Beginn, anfrage.Ende) is { } fehler)
        {
            return Results.BadRequest(new { fehler });
        }

        var freizeit = new Freizeit
        {
            Id = Guid.NewGuid(),
            TenantId = treffer.Organisation.Id,
            Name = anfrage.Name.Trim(),
            Beginn = anfrage.Beginn,
            Ende = anfrage.Ende,
            Ort = Leer(anfrage.Ort),
            Status = FreizeitStatus.Offen,
            ErstelltAm = DateTimeOffset.UtcNow,
        };

        datenbank.Freizeiten.Add(freizeit);
        await datenbank.SaveChangesAsync(abbruch);

        return Results.Created($"/api/o/{slug}/freizeiten/{freizeit.Id}", new FreizeitDto(
            freizeit.Id, freizeit.Name, freizeit.Beginn, freizeit.Ende, freizeit.Ort,
            freizeit.Status, treffer.Organisation.Id, treffer.Organisation.Name,
            treffer.Organisation.Slug, null, true));
    }

    private static async Task<IResult> EinzelnAsync(
        string slug,
        Guid id,
        ClaimsPrincipal angemeldet,
        Organisationsaufloeser aufloeser,
        IBerechtigung berechtigung,
        ZeltlotseDbContext datenbank,
        CancellationToken abbruch)
    {
        var treffer = await aufloeser.AufloesenAsync(slug, abbruch);

        if (treffer is null)
        {
            return Organisationsaufloeser.KeinZugriff;
        }

        var nutzerId = angemeldet.NutzerId();
        var rechte = await berechtigung.FuerFreizeitAsync(nutzerId, id, abbruch);

        if (!rechte.DarfLesen)
        {
            return Organisationsaufloeser.KeinZugriff;
        }

        var freizeit = await datenbank.Freizeiten
            .Aktiv()
            .Include(f => f.Organisation)
            .Include(f => f.Zuordnungen)
            .Where(f => f.Id == id)
            .Select(f => Abbilden(f, nutzerId, rechte.DarfEckdatenAendern))
            .FirstOrDefaultAsync(abbruch);

        return freizeit is null ? Organisationsaufloeser.KeinZugriff : Results.Ok(freizeit);
    }

    private static async Task<IResult> AendernAsync(
        string slug,
        Guid id,
        FreizeitAendern anfrage,
        ClaimsPrincipal angemeldet,
        Organisationsaufloeser aufloeser,
        IBerechtigung berechtigung,
        ZeltlotseDbContext datenbank,
        CancellationToken abbruch)
    {
        var treffer = await aufloeser.AufloesenAsync(slug, abbruch);

        if (treffer is null)
        {
            return Organisationsaufloeser.KeinZugriff;
        }

        var rechte = await berechtigung.FuerFreizeitAsync(angemeldet.NutzerId(), id, abbruch);

        if (!rechte.DarfEckdatenAendern)
        {
            return Organisationsaufloeser.KeinZugriff;
        }

        if (Pruefen(anfrage.Name, anfrage.Beginn, anfrage.Ende) is { } fehler)
        {
            return Results.BadRequest(new { fehler });
        }

        var freizeit = await datenbank.Freizeiten.Aktiv().FirstOrDefaultAsync(f => f.Id == id, abbruch);

        if (freizeit is null)
        {
            return Organisationsaufloeser.KeinZugriff;
        }

        freizeit.Name = anfrage.Name.Trim();
        freizeit.Beginn = anfrage.Beginn;
        freizeit.Ende = anfrage.Ende;
        freizeit.Ort = Leer(anfrage.Ort);
        freizeit.Status = anfrage.Status;

        await datenbank.SaveChangesAsync(abbruch);

        return Results.NoContent();
    }

    private static async Task<IResult> LoeschenAsync(
        string slug,
        Guid id,
        ClaimsPrincipal angemeldet,
        Organisationsaufloeser aufloeser,
        IBerechtigung berechtigung,
        ZeltlotseDbContext datenbank,
        CancellationToken abbruch)
    {
        var treffer = await aufloeser.AufloesenAsync(slug, abbruch);

        if (treffer is null)
        {
            return Organisationsaufloeser.KeinZugriff;
        }

        var rechte = await berechtigung.FuerFreizeitAsync(angemeldet.NutzerId(), id, abbruch);

        if (!rechte.DarfLoeschen)
        {
            return Organisationsaufloeser.KeinZugriff;
        }

        var freizeit = await datenbank.Freizeiten.Aktiv().FirstOrDefaultAsync(f => f.Id == id, abbruch);

        if (freizeit is null)
        {
            return Organisationsaufloeser.KeinZugriff;
        }

        // Weich gelöscht: 30 Tage im Papierkorb, dann räumt der Hintergrunddienst auf.
        freizeit.GeloeschtAm = DateTimeOffset.UtcNow;
        await datenbank.SaveChangesAsync(abbruch);

        return Results.NoContent();
    }

    private static async Task<IResult> PapierkorbAsync(
        string slug,
        Organisationsaufloeser aufloeser,
        ZeltlotseDbContext datenbank,
        CancellationToken abbruch)
    {
        var treffer = await aufloeser.AufloesenAsync(slug, abbruch);

        if (treffer is null || !treffer.Rechte.DarfVerwalten)
        {
            return Organisationsaufloeser.KeinZugriff;
        }

        var jetzt = DateTimeOffset.UtcNow;

        var eintraege = await datenbank.Freizeiten
            .Where(f => f.GeloeschtAm != null)
            .OrderByDescending(f => f.GeloeschtAm)
            .Select(f => new PapierkorbEintragDto(
                f.Id,
                f.Name,
                f.GeloeschtAm!.Value,
                PapierkorbTage - (int)(jetzt - f.GeloeschtAm!.Value).TotalDays))
            .ToListAsync(abbruch);

        return Results.Ok(eintraege);
    }

    private static async Task<IResult> WiederherstellenAsync(
        string slug,
        Guid id,
        Organisationsaufloeser aufloeser,
        ZeltlotseDbContext datenbank,
        CancellationToken abbruch)
    {
        var treffer = await aufloeser.AufloesenAsync(slug, abbruch);

        if (treffer is null || !treffer.Rechte.DarfVerwalten)
        {
            return Organisationsaufloeser.KeinZugriff;
        }

        var freizeit = await datenbank.Freizeiten
            .FirstOrDefaultAsync(f => f.Id == id && f.GeloeschtAm != null, abbruch);

        if (freizeit is null)
        {
            return Results.NotFound();
        }

        freizeit.GeloeschtAm = null;
        await datenbank.SaveChangesAsync(abbruch);

        return Results.NoContent();
    }

    // ---------- Team ----------

    private static async Task<IResult> TeamAsync(
        string slug,
        Guid id,
        ClaimsPrincipal angemeldet,
        Organisationsaufloeser aufloeser,
        IBerechtigung berechtigung,
        ZeltlotseDbContext datenbank,
        CancellationToken abbruch)
    {
        var treffer = await aufloeser.AufloesenAsync(slug, abbruch);

        if (treffer is null
            || !(await berechtigung.FuerFreizeitAsync(angemeldet.NutzerId(), id, abbruch)).DarfLesen)
        {
            return Organisationsaufloeser.KeinZugriff;
        }

        var team = await datenbank.FreizeitZuordnungen
            .Where(z => z.FreizeitId == id)
            .OrderByDescending(z => z.Rolle)
            .ThenBy(z => z.Nutzer!.Email)
            .Select(z => new FreizeitTeamDto(z.NutzerId, z.Nutzer!.Email ?? string.Empty, z.Rolle))
            .ToListAsync(abbruch);

        return Results.Ok(team);
    }

    private static async Task<IResult> TeamHinzufuegenAsync(
        string slug,
        Guid id,
        FreizeitTeamDto anfrage,
        ClaimsPrincipal angemeldet,
        Organisationsaufloeser aufloeser,
        IBerechtigung berechtigung,
        ZeltlotseDbContext datenbank,
        CancellationToken abbruch)
    {
        var treffer = await aufloeser.AufloesenAsync(slug, abbruch);

        if (treffer is null
            || !(await berechtigung.FuerFreizeitAsync(angemeldet.NutzerId(), id, abbruch))
                .DarfTeamVerwalten)
        {
            return Organisationsaufloeser.KeinZugriff;
        }

        var istMitglied = await datenbank.OrgMitgliedschaften
            .AnyAsync(m => m.NutzerId == anfrage.NutzerId
                && m.OrganisationId == treffer.Organisation.Id, abbruch);

        if (!istMitglied)
        {
            return Results.BadRequest(new
            {
                fehler = "Diese Person gehört nicht zur Organisation. Lade sie zuerst ein.",
            });
        }

        var vorhanden = await datenbank.FreizeitZuordnungen
            .FirstOrDefaultAsync(z => z.NutzerId == anfrage.NutzerId && z.FreizeitId == id, abbruch);

        if (vorhanden is null)
        {
            datenbank.FreizeitZuordnungen.Add(new FreizeitZuordnung
            {
                NutzerId = anfrage.NutzerId,
                FreizeitId = id,
                TenantId = treffer.Organisation.Id,
                Rolle = anfrage.Rolle,
            });
        }
        else
        {
            vorhanden.Rolle = anfrage.Rolle;
        }

        await datenbank.SaveChangesAsync(abbruch);

        return Results.NoContent();
    }

    private static async Task<IResult> TeamEntfernenAsync(
        string slug,
        Guid id,
        Guid nutzerId,
        ClaimsPrincipal angemeldet,
        Organisationsaufloeser aufloeser,
        IBerechtigung berechtigung,
        ZeltlotseDbContext datenbank,
        CancellationToken abbruch)
    {
        var treffer = await aufloeser.AufloesenAsync(slug, abbruch);

        if (treffer is null
            || !(await berechtigung.FuerFreizeitAsync(angemeldet.NutzerId(), id, abbruch))
                .DarfTeamVerwalten)
        {
            return Organisationsaufloeser.KeinZugriff;
        }

        await datenbank.FreizeitZuordnungen
            .Where(z => z.FreizeitId == id && z.NutzerId == nutzerId)
            .ExecuteDeleteAsync(abbruch);

        return Results.NoContent();
    }

    /// <summary>
    /// Die Freizeitleitung darf Menschen neu in die Organisation holen — so
    /// steht es in der Delegationskette.
    /// </summary>
    private static async Task<IResult> TeamEinladenAsync(
        string slug,
        Guid id,
        EinladungAnlegen anfrage,
        ClaimsPrincipal angemeldet,
        Organisationsaufloeser aufloeser,
        IBerechtigung berechtigung,
        IEinladungen einladungen,
        CancellationToken abbruch)
    {
        var treffer = await aufloeser.AufloesenAsync(slug, abbruch);

        if (treffer is null
            || !(await berechtigung.FuerFreizeitAsync(angemeldet.NutzerId(), id, abbruch))
                .DarfTeamVerwalten)
        {
            return Organisationsaufloeser.KeinZugriff;
        }

        var bereinigt = anfrage with
        {
            Ziel = Einladungsziel.Freizeit,
            OrgRolle = OrgRolle.Mitglied,
            FreizeitId = id,
            FreizeitRolle = anfrage.FreizeitRolle ?? FreizeitRolle.Mitarbeiter,
        };

        return Results.Ok(await einladungen.ErstellenAsync(
            treffer.Organisation.Id, angemeldet.NutzerId(), bereinigt, abbruch));
    }

    // ---------- Hilfen ----------

    private static string? Pruefen(string? name, DateOnly? beginn, DateOnly? ende)
    {
        var bereinigt = (name ?? string.Empty).Trim();

        if (bereinigt.Length == 0)
        {
            return "Die Freizeit braucht einen Namen.";
        }

        if (bereinigt.Length > 120)
        {
            return "Der Name ist zu lang (höchstens 120 Zeichen).";
        }

        if (beginn is not null && ende is not null && ende < beginn)
        {
            return "Das Ende liegt vor dem Beginn.";
        }

        return null;
    }

    private static string? Leer(string? wert)
        => string.IsNullOrWhiteSpace(wert) ? null : wert.Trim();

    private static FreizeitDto Abbilden(Freizeit f, Guid nutzerId, bool darfVerwalten)
        => new(
            f.Id,
            f.Name,
            f.Beginn,
            f.Ende,
            f.Ort,
            f.Status,
            f.TenantId,
            f.Organisation!.Name,
            f.Organisation.Slug,
            f.Zuordnungen
                .Where(z => z.NutzerId == nutzerId)
                .Select(z => (FreizeitRolle?)z.Rolle)
                .FirstOrDefault(),
            darfVerwalten);
}
