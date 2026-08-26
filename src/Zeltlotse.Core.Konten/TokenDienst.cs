using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Zeltlotse.Core.Konten.Contracts;
using Zeltlotse.Core.Persistenz;

namespace Zeltlotse.Core.Konten;

public sealed class TokenEinstellungen
{
    public const string Abschnitt = "Zeltlotse:Token";

    /// <summary>Symmetrischer Schlüssel, mindestens 32 Byte. Lokal aus User Secrets, produktiv aus dem Key Vault.</summary>
    public string Schluessel { get; set; } = string.Empty;

    public string Aussteller { get; set; } = "zeltlotse";

    public string Empfaenger { get; set; } = "zeltlotse";

    public int ZugriffMinuten { get; set; } = 15;

    public int ErneuerungTage { get; set; } = 30;
}

/// <summary>
/// Stellt Zugriffs- und Erneuerungstoken aus. Das Zugriffstoken trägt die
/// Organisationen des Nutzers als Claim — daraus speist sich die
/// Row-Level-Security, siehe <see cref="MandantInterceptor"/>.
/// </summary>
internal sealed class TokenDienst(ZeltlotseDbContext datenbank, TokenEinstellungen einstellungen)
{
    public async Task<(string Token, int GueltigSekunden)> ZugriffstokenAsync(
        Nutzer nutzer, CancellationToken abbruch)
    {
        var organisationen = await datenbank.OrgMitgliedschaften
            .Where(m => m.NutzerId == nutzer.Id)
            .Select(m => m.OrganisationId)
            .ToListAsync(abbruch);

        var ansprueche = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, nutzer.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, nutzer.Email ?? string.Empty),
            new(Ansprueche.Organisationen, string.Join(',', organisationen)),
        };

        if (nutzer.IstGlobalAdmin)
        {
            ansprueche.Add(new Claim(Ansprueche.GlobalAdmin, "1"));
        }

        var schluessel = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(einstellungen.Schluessel));
        var gueltigSekunden = einstellungen.ZugriffMinuten * 60;

        var token = new JwtSecurityToken(
            issuer: einstellungen.Aussteller,
            audience: einstellungen.Empfaenger,
            claims: ansprueche,
            expires: DateTime.UtcNow.AddMinutes(einstellungen.ZugriffMinuten),
            signingCredentials: new SigningCredentials(schluessel, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), gueltigSekunden);
    }

    /// <summary>Gibt den Klartext zurück; gespeichert wird nur der Hash.</summary>
    public async Task<string> ErneuerungstokenAsync(Guid nutzerId, CancellationToken abbruch)
    {
        var klartext = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

        datenbank.Erneuerungstoken.Add(new Erneuerungstoken
        {
            Id = Guid.NewGuid(),
            NutzerId = nutzerId,
            TokenHash = Hash(klartext),
            GueltigBis = DateTimeOffset.UtcNow.AddDays(einstellungen.ErneuerungTage),
        });

        await datenbank.SaveChangesAsync(abbruch);

        return klartext;
    }

    /// <summary>
    /// Löst ein Erneuerungstoken ein und dreht es dabei. Ein bereits benutztes
    /// Token ist ein Diebstahlsignal: Dann fliegen alle Token dieses Nutzers.
    /// </summary>
    public async Task<Nutzer?> EinloesenAsync(string klartext, CancellationToken abbruch)
    {
        var hash = Hash(klartext);

        var vorhanden = await datenbank.Erneuerungstoken
            .Include(t => t.Nutzer)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, abbruch);

        if (vorhanden is null)
        {
            return null;
        }

        if (vorhanden.VerwendetAm is not null)
        {
            await AlleEntwertenAsync(vorhanden.NutzerId, abbruch);
            return null;
        }

        if (vorhanden.GueltigBis < DateTimeOffset.UtcNow || vorhanden.Nutzer is null
            || vorhanden.Nutzer.Gesperrt)
        {
            return null;
        }

        vorhanden.VerwendetAm = DateTimeOffset.UtcNow;
        await datenbank.SaveChangesAsync(abbruch);

        return vorhanden.Nutzer;
    }

    public async Task EntwertenAsync(string klartext, CancellationToken abbruch)
    {
        var hash = Hash(klartext);

        await datenbank.Erneuerungstoken
            .Where(t => t.TokenHash == hash)
            .ExecuteDeleteAsync(abbruch);
    }

    public Task AlleEntwertenAsync(Guid nutzerId, CancellationToken abbruch)
        => datenbank.Erneuerungstoken
            .Where(t => t.NutzerId == nutzerId)
            .ExecuteDeleteAsync(abbruch);

    public static string Hash(string klartext)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(klartext)));
}
