using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Core.Konten.Contracts;
using Zeltlotse.Core.Organisationen.Contracts;
using Zeltlotse.Core.Persistenz;

namespace Zeltlotse.Core.Konten;

public static class KontenErweiterungen
{
    /// <summary>Trägt das Erneuerungstoken. Verlässt den Pfad /api/auth nie.</summary>
    public const string ErneuerungCookie = "zl_erneuerung";

    /// <summary>
    /// Muss bei jedem Aufruf gesetzt sein, der auf das Cookie baut. Ein Formular
    /// von fremder Seite kann keinen eigenen Kopfzeilenwert setzen — damit ist
    /// die Erneuerung nicht fremdauslösbar.
    /// </summary>
    public const string AnfrageHeader = "X-Zeltlotse-Anfrage";

    public static IServiceCollection AddKonten(this IServiceCollection dienste)
    {
        dienste.AddScoped<TokenDienst>();
        dienste.AddScoped<Einladungsdienst>();
        dienste.AddScoped<IEinladungen>(sp => sp.GetRequiredService<Einladungsdienst>());

        dienste.AddIdentityCore<Nutzer>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = 10;
                o.Password.RequireNonAlphanumeric = false;
            })
            .AddEntityFrameworkStores<ZeltlotseDbContext>();

        return dienste;
    }

    public static IEndpointRouteBuilder MapKonten(this IEndpointRouteBuilder route)
    {
        var einrichtung = route.MapGroup("/api/einrichtung");

        einrichtung.MapGet("/noetig", async (ZeltlotseDbContext db, CancellationToken ct)
            => Results.Ok(!await db.Users.AnyAsync(n => n.IstGlobalAdmin, ct)));

        einrichtung.MapPost("/", EinrichtenAsync);

        var auth = route.MapGroup("/api/auth");

        auth.MapPost("/anmelden", AnmeldenAsync);
        auth.MapPost("/erneuern", ErneuernAsync);
        auth.MapPost("/abmelden", AbmeldenAsync);

        route.MapGet("/api/ich", IchAsync).RequireAuthorization();

        route.MapGet("/api/einladungen/{token}", VorschauAsync);
        route.MapPost("/api/einladungen/einloesen", EinloesenAsync);

        route.MapPost("/api/konto/kennwort", KennwortAendernAsync).RequireAuthorization();

        var konten = route.MapGroup("/api/verwaltung/konten").RequireAuthorization();

        konten.MapGet("/", KontenAsync);
        konten.MapPost("/{id:guid}/sperre", SperreAsync);

        return route;
    }

    /// <summary>
    /// Kontenliste des Betreibers. Bewusst ohne jede Spalte, die verrät, zu
    /// welchen Organisationen jemand gehört — das wäre der Einblick, den der
    /// Betreiber nicht haben soll.
    /// </summary>
    private static async Task<IResult> KontenAsync(
        ClaimsPrincipal angemeldet, ZeltlotseDbContext datenbank, CancellationToken abbruch)
    {
        if (!angemeldet.IstGlobalAdmin())
        {
            return Results.Forbid();
        }

        var konten = await datenbank.Users
            .OrderBy(n => n.Email)
            .Select(n => new KontoDto(n.Id, n.Email ?? string.Empty, n.IstGlobalAdmin,
                n.Gesperrt, n.LetzteAnmeldung))
            .ToListAsync(abbruch);

        return Results.Ok(konten);
    }

    private static async Task<IResult> SperreAsync(
        Guid id,
        bool gesperrt,
        ClaimsPrincipal angemeldet,
        ZeltlotseDbContext datenbank,
        TokenDienst token,
        CancellationToken abbruch)
    {
        if (!angemeldet.IstGlobalAdmin())
        {
            return Results.Forbid();
        }

        if (id == angemeldet.NutzerId())
        {
            return Results.BadRequest(new { fehler = "Du kannst dich nicht selbst sperren." });
        }

        var nutzer = await datenbank.Users.FirstOrDefaultAsync(n => n.Id == id, abbruch);

        if (nutzer is null)
        {
            return Results.NotFound();
        }

        nutzer.Gesperrt = gesperrt;
        await datenbank.SaveChangesAsync(abbruch);

        if (gesperrt)
        {
            // Eine Sperre, die erst mit dem nächsten Token greift, ist keine.
            await token.AlleEntwertenAsync(id, abbruch);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> KennwortAendernAsync(
        KennwortAendern anfrage,
        ClaimsPrincipal angemeldet,
        UserManager<Nutzer> nutzerverwaltung,
        CancellationToken abbruch)
    {
        var nutzer = await nutzerverwaltung.FindByIdAsync(angemeldet.NutzerId().ToString());

        if (nutzer is null)
        {
            return Results.Unauthorized();
        }

        var ergebnis = await nutzerverwaltung.ChangePasswordAsync(nutzer, anfrage.Alt, anfrage.Neu);

        return ergebnis.Succeeded ? Results.NoContent() : Fehler(ergebnis);
    }

    /// <summary>
    /// Legt den ersten GlobalAdmin an. Danach ist dieser Weg dauerhaft zu —
    /// die Selbstabschaltung ist die einzige Sicherung, bewusst so entschieden
    /// (siehe debt.md).
    /// </summary>
    private static async Task<IResult> EinrichtenAsync(
        EinrichtungAnfrage anfrage,
        UserManager<Nutzer> nutzerverwaltung,
        ZeltlotseDbContext datenbank,
        TokenDienst token,
        HttpContext http,
        CancellationToken abbruch)
    {
        if (await datenbank.Users.AnyAsync(n => n.IstGlobalAdmin, abbruch))
        {
            return Results.Conflict(new { fehler = "Zeltlotse ist bereits eingerichtet." });
        }

        var nutzer = new Nutzer
        {
            Id = Guid.NewGuid(),
            UserName = anfrage.EMail,
            Email = anfrage.EMail,
            IstGlobalAdmin = true,
            ErstelltAm = DateTimeOffset.UtcNow,
        };

        var ergebnis = await nutzerverwaltung.CreateAsync(nutzer, anfrage.Kennwort);

        if (!ergebnis.Succeeded)
        {
            return Fehler(ergebnis);
        }

        return await AnmeldungAbschliessenAsync(nutzer, token, datenbank, http, abbruch);
    }

    private static async Task<IResult> AnmeldenAsync(
        AnmeldungAnfrage anfrage,
        UserManager<Nutzer> nutzerverwaltung,
        ZeltlotseDbContext datenbank,
        TokenDienst token,
        HttpContext http,
        CancellationToken abbruch)
    {
        var nutzer = await nutzerverwaltung.FindByEmailAsync(anfrage.EMail);

        if (nutzer is null || nutzer.Gesperrt
            || !await nutzerverwaltung.CheckPasswordAsync(nutzer, anfrage.Kennwort))
        {
            // Bewusst dieselbe Antwort für „gibt es nicht" und „falsches Kennwort".
            return Results.Unauthorized();
        }

        nutzer.LetzteAnmeldung = DateTimeOffset.UtcNow;
        await datenbank.SaveChangesAsync(abbruch);

        return await AnmeldungAbschliessenAsync(nutzer, token, datenbank, http, abbruch);
    }

    private static async Task<IResult> ErneuernAsync(
        TokenDienst token,
        ZeltlotseDbContext datenbank,
        HttpContext http,
        CancellationToken abbruch)
    {
        if (!http.Request.Headers.ContainsKey(AnfrageHeader))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var klartext = http.Request.Cookies[ErneuerungCookie];

        if (string.IsNullOrEmpty(klartext))
        {
            return Results.Unauthorized();
        }

        var nutzer = await token.EinloesenAsync(klartext, abbruch);

        if (nutzer is null)
        {
            CookieLoeschen(http);
            return Results.Unauthorized();
        }

        return await AnmeldungAbschliessenAsync(nutzer, token, datenbank, http, abbruch);
    }

    private static async Task<IResult> AbmeldenAsync(
        TokenDienst token,
        HttpContext http,
        CancellationToken abbruch)
    {
        var klartext = http.Request.Cookies[ErneuerungCookie];

        if (!string.IsNullOrEmpty(klartext))
        {
            await token.EntwertenAsync(klartext, abbruch);
        }

        CookieLoeschen(http);

        return Results.NoContent();
    }

    private static async Task<IResult> IchAsync(
        ClaimsPrincipal angemeldet,
        ZeltlotseDbContext datenbank,
        CancellationToken abbruch)
    {
        var id = angemeldet.NutzerId();

        var nutzer = await datenbank.Users.FirstOrDefaultAsync(n => n.Id == id, abbruch);

        if (nutzer is null)
        {
            return Results.Unauthorized();
        }

        var organisationen = await datenbank.OrgMitgliedschaften
            .Where(m => m.NutzerId == id && m.Organisation!.GeloeschtAm == null)
            .OrderBy(m => m.Organisation!.Name)
            .Select(m => new OrganisationDto(
                m.OrganisationId,
                m.Organisation!.Name,
                m.Organisation.Slug,
                m.Rolle,
                m.Organisation.LoeschungBeantragtAm))
            .ToListAsync(abbruch);

        return Results.Ok(new AngemeldeterNutzerDto(
            nutzer.Id, nutzer.Email ?? string.Empty, nutzer.IstGlobalAdmin, organisationen));
    }

    private static async Task<IResult> VorschauAsync(
        string token,
        Einladungsdienst einladungen,
        CancellationToken abbruch)
    {
        var vorschau = await einladungen.VorschauAsync(token, abbruch);

        return vorschau is null
            ? Results.NotFound(new { fehler = "Diese Einladung ist abgelaufen oder bereits benutzt." })
            : Results.Ok(vorschau);
    }

    private static async Task<IResult> EinloesenAsync(
        EinladungEinloesen anfrage,
        Einladungsdienst einladungen,
        ZeltlotseDbContext datenbank,
        TokenDienst token,
        HttpContext http,
        CancellationToken abbruch)
    {
        var (nutzer, fehler) = await einladungen.EinloesenAsync(anfrage, abbruch);

        if (nutzer is null)
        {
            return Results.BadRequest(new { fehler });
        }

        return await AnmeldungAbschliessenAsync(nutzer, token, datenbank, http, abbruch);
    }

    private static async Task<IResult> AnmeldungAbschliessenAsync(
        Nutzer nutzer,
        TokenDienst token,
        ZeltlotseDbContext datenbank,
        HttpContext http,
        CancellationToken abbruch)
    {
        var (zugriff, gueltig) = await token.ZugriffstokenAsync(nutzer, abbruch);
        var erneuerung = await token.ErneuerungstokenAsync(nutzer.Id, abbruch);

        http.Response.Cookies.Append(ErneuerungCookie, erneuerung, new CookieOptions
        {
            HttpOnly = true,
            Secure = http.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/api/auth",
            Expires = DateTimeOffset.UtcNow.AddDays(30),
        });

        return Results.Ok(new AnmeldungAntwort(zugriff, gueltig));
    }

    private static void CookieLoeschen(HttpContext http)
        => http.Response.Cookies.Delete(ErneuerungCookie, new CookieOptions { Path = "/api/auth" });

    private static IResult Fehler(IdentityResult ergebnis)
        => Results.BadRequest(new
        {
            fehler = string.Join(" ", ergebnis.Errors.Select(f => f.Description)),
        });

}
