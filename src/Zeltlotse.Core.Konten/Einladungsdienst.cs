using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Zeltlotse.Core.Freizeiten.Contracts;
using Zeltlotse.Core.Konten.Contracts;
using Zeltlotse.Core.Organisationen.Contracts;
using Zeltlotse.Core.Persistenz;

namespace Zeltlotse.Core.Konten;

public sealed class EinladungsEinstellungen
{
    public const string Abschnitt = "Zeltlotse:Einladung";

    /// <summary>Adresse der Oberfläche — der erzeugte Link zeigt dorthin.</summary>
    public string Basisadresse { get; set; } = "https://app.zeltlotse.de";

    public int GueltigTage { get; set; } = 14;
}

internal sealed class Einladungsdienst(
    ZeltlotseDbContext datenbank,
    SystemDatenbank system,
    UserManager<Nutzer> nutzerverwaltung,
    EinladungsEinstellungen einstellungen)
    : IEinladungen
{
    public async Task<IReadOnlyList<OffeneEinladungDto>> OffeneAsync(
        Guid organisationId, CancellationToken abbruch)
        => await datenbank.Einladungen
            .Where(e => e.TenantId == organisationId
                && e.EingeloestAm == null
                && e.GueltigBis > DateTimeOffset.UtcNow)
            .OrderBy(e => e.GueltigBis)
            .Select(e => new OffeneEinladungDto(
                e.Id,
                e.EMail,
                e.Ziel == Einladungsziel.Freizeit ? "Freizeit" : "Organisation",
                e.GueltigBis))
            .ToListAsync(abbruch);

    public async Task<EinladungErzeugtDto> ErstellenAsync(
        Guid organisationId, Guid ersteller, EinladungAnlegen anfrage, CancellationToken abbruch)
    {
        var klartext = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

        var einladung = new Einladung
        {
            Id = Guid.NewGuid(),
            TenantId = organisationId,
            EMail = anfrage.EMail.Trim(),
            Ziel = anfrage.Ziel,
            OrgRolle = anfrage.OrgRolle ?? OrgRolle.Mitglied,
            FreizeitRolle = anfrage.FreizeitRolle,
            FreizeitId = anfrage.FreizeitId,
            TokenHash = TokenDienst.Hash(klartext),
            GueltigBis = DateTimeOffset.UtcNow.AddDays(einstellungen.GueltigTage),
            ErstelltVon = ersteller,
        };

        datenbank.Einladungen.Add(einladung);
        await datenbank.SaveChangesAsync(abbruch);

        return new EinladungErzeugtDto(
            einladung.Id,
            einladung.EMail,
            $"{einstellungen.Basisadresse.TrimEnd('/')}/einladung/{klartext}",
            einladung.GueltigBis);
    }

    public async Task<EinladungVorschauDto?> VorschauAsync(string klartext, CancellationToken abbruch)
    {
        await using var ohneSchranke = system.Oeffnen();

        var einladung = await GueltigeEinladungAsync(ohneSchranke, klartext, abbruch);

        if (einladung is null)
        {
            return null;
        }

        var name = await datenbank.Organisationen
            .Where(o => o.Id == einladung.TenantId)
            .Select(o => o.Name)
            .FirstOrDefaultAsync(abbruch);

        return new EinladungVorschauDto(einladung.EMail, name ?? string.Empty);
    }

    public async Task<(Nutzer? Nutzer, string? Fehler)> EinloesenAsync(
        EinladungEinloesen anfrage, CancellationToken abbruch)
    {
        // Nur die Einladung selbst und ihre Folgezeilen laufen ohne Schranke;
        // Konto und Organisationsmitgliedschaft tragen ohnehin keine.
        await using var ohneSchranke = system.Oeffnen();

        var einladung = await GueltigeEinladungAsync(ohneSchranke, anfrage.Token, abbruch);

        if (einladung is null)
        {
            return (null, "Diese Einladung ist abgelaufen oder wurde bereits benutzt.");
        }

        var nutzer = await nutzerverwaltung.FindByEmailAsync(einladung.EMail);

        if (nutzer is null)
        {
            nutzer = new Nutzer
            {
                Id = Guid.NewGuid(),
                UserName = einladung.EMail,
                Email = einladung.EMail,
                ErstelltAm = DateTimeOffset.UtcNow,
            };

            var ergebnis = await nutzerverwaltung.CreateAsync(nutzer, anfrage.Kennwort);

            if (!ergebnis.Succeeded)
            {
                return (null, string.Join(" ", ergebnis.Errors.Select(f => f.Description)));
            }
        }

        await ZuordnenAsync(ohneSchranke, einladung, nutzer.Id, abbruch);

        einladung.EingeloestAm = DateTimeOffset.UtcNow;
        nutzer.LetzteAnmeldung = DateTimeOffset.UtcNow;

        await ohneSchranke.SaveChangesAsync(abbruch);
        await datenbank.SaveChangesAsync(abbruch);

        return (nutzer, null);
    }

    private async Task ZuordnenAsync(
        ZeltlotseDbContext ohneSchranke, Einladung einladung, Guid nutzerId, CancellationToken abbruch)
    {
        var vorhanden = await datenbank.OrgMitgliedschaften
            .FirstOrDefaultAsync(m => m.NutzerId == nutzerId
                && m.OrganisationId == einladung.TenantId, abbruch);

        if (vorhanden is null)
        {
            datenbank.OrgMitgliedschaften.Add(new OrgMitgliedschaft
            {
                NutzerId = nutzerId,
                OrganisationId = einladung.TenantId,
                Rolle = einladung.OrgRolle,
                SeitAm = DateTimeOffset.UtcNow,
            });
        }
        else if (einladung.OrgRolle > vorhanden.Rolle)
        {
            // Rollen sind additiv: die weitergehende gewinnt, nie die schwächere.
            vorhanden.Rolle = einladung.OrgRolle;
        }

        if (einladung.Ziel != Einladungsziel.Freizeit || einladung.FreizeitId is not { } freizeitId)
        {
            return;
        }

        var zuordnung = await ohneSchranke.FreizeitZuordnungen
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(z => z.NutzerId == nutzerId && z.FreizeitId == freizeitId, abbruch);

        if (zuordnung is null)
        {
            ohneSchranke.FreizeitZuordnungen.Add(new FreizeitZuordnung
            {
                NutzerId = nutzerId,
                FreizeitId = freizeitId,
                TenantId = einladung.TenantId,
                Rolle = einladung.FreizeitRolle ?? FreizeitRolle.Mitarbeiter,
            });
        }
        else if ((einladung.FreizeitRolle ?? FreizeitRolle.Mitarbeiter) > zuordnung.Rolle)
        {
            zuordnung.Rolle = einladung.FreizeitRolle ?? FreizeitRolle.Mitarbeiter;
        }
    }

    private static Task<Einladung?> GueltigeEinladungAsync(
        ZeltlotseDbContext ohneSchranke, string klartext, CancellationToken abbruch)
    {
        var hash = TokenDienst.Hash(klartext);

        return ohneSchranke.Einladungen
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.TokenHash == hash
                && e.EingeloestAm == null
                && e.GueltigBis > DateTimeOffset.UtcNow, abbruch);
    }

}
