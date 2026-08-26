using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Zeltlotse.Core.Persistenz;

public sealed record Organisationstreffer(Organisation Organisation, OrgRechte Rechte);

/// <summary>
/// Übersetzt den Slug aus der Adresse in eine Organisation, prüft die
/// Zugehörigkeit des Angemeldeten und setzt den Mandanten für alle folgenden
/// Abfragen. Muss vor jeder mandantenbehafteten Abfrage gelaufen sein.
/// </summary>
public sealed class Organisationsaufloeser(
    ZeltlotseDbContext datenbank,
    MandantKontext mandant,
    IBerechtigung berechtigung,
    IHttpContextAccessor kontext)
{
    /// <summary>
    /// Dieselbe Antwort für „gibt es nicht" und „gehört dir nicht" — sonst
    /// verrät die Anwendung, welche Organisationen existieren.
    /// </summary>
    public static IResult KeinZugriff => Results.NotFound(new
    {
        fehler = "Diese Seite gehört zu einer Organisation, zu der du nicht gehörst.",
    });

    public async Task<Organisationstreffer?> AufloesenAsync(string slug, CancellationToken abbruch)
    {
        var angemeldet = kontext.HttpContext?.User;

        if (angemeldet?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var organisation = await datenbank.Organisationen
            .Aktiv()
            .FirstOrDefaultAsync(o => o.Slug == slug, abbruch);

        if (organisation is null)
        {
            return null;
        }

        var rechte = await berechtigung.FuerOrganisationAsync(
            NutzerId(angemeldet), organisation.Id, abbruch);

        if (!rechte.DarfLesen)
        {
            return null;
        }

        mandant.AktuellerMandant = organisation.Id;

        return new Organisationstreffer(organisation, rechte);
    }

    private static Guid NutzerId(ClaimsPrincipal angemeldet)
        => Guid.TryParse(
            angemeldet.FindFirstValue("sub") ?? angemeldet.FindFirstValue(ClaimTypes.NameIdentifier),
            out var id)
            ? id
            : Guid.Empty;
}
