using Microsoft.AspNetCore.Identity;
using Zeltlotse.Core.Freizeiten.Contracts;
using Zeltlotse.Core.Organisationen.Contracts;
using Zeltlotse.Core.Konten.Contracts;

namespace Zeltlotse.Core.Persistenz;

/// <summary>
/// Ein Mensch. Genau ein Konto, beliebig viele Zuordnungen. GlobalAdmin ist
/// ein Kennzeichen am Konto, keine Zuordnung — der Betreiber gehört zu keiner
/// Organisation.
/// </summary>
public sealed class Nutzer : IdentityUser<Guid>
{
    /// <summary>
    /// Anzeigename. Eine Liste aus lauter E-Mail-Adressen beantwortet die
    /// Frage nicht, wer da eigentlich steht.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    public bool IstGlobalAdmin { get; set; }

    public bool Gesperrt { get; set; }

    public DateTimeOffset ErstelltAm { get; set; }

    public DateTimeOffset? LetzteAnmeldung { get; set; }
}

/// <summary>Ein Träger: Gemeinde, Werk, Verband. Der Mandant.</summary>
public sealed class Organisation
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Adresse unter /o/{slug}. Einmal vergeben, nie geändert.</summary>
    public required string Slug { get; set; }

    public DateTimeOffset ErstelltAm { get; set; }

    /// <summary>Von der Organisationsleitung gestellt, vom Betreiber ausgeführt.</summary>
    public DateTimeOffset? LoeschungBeantragtAm { get; set; }

    public DateTimeOffset? GeloeschtAm { get; set; }

    public ICollection<OrgMitgliedschaft> Mitgliedschaften { get; set; } = [];

    public ICollection<Freizeit> Freizeiten { get; set; } = [];
}

public sealed class OrgMitgliedschaft
{
    public Guid NutzerId { get; set; }

    public Guid OrganisationId { get; set; }

    public OrgRolle Rolle { get; set; }

    public DateTimeOffset SeitAm { get; set; }

    public Nutzer? Nutzer { get; set; }

    public Organisation? Organisation { get; set; }
}

public sealed class Freizeit
{
    public Guid Id { get; set; }

    /// <summary>Die Organisation, der diese Freizeit gehört. Trägt die Mandantentrennung.</summary>
    public Guid TenantId { get; set; }

    public required string Name { get; set; }

    public DateOnly? Beginn { get; set; }

    public DateOnly? Ende { get; set; }

    public string? Ort { get; set; }

    public FreizeitStatus Status { get; set; }

    public DateTimeOffset ErstelltAm { get; set; }

    public DateTimeOffset? GeloeschtAm { get; set; }

    public Organisation? Organisation { get; set; }

    public ICollection<FreizeitZuordnung> Zuordnungen { get; set; } = [];
}

public sealed class FreizeitZuordnung
{
    public Guid NutzerId { get; set; }

    public Guid FreizeitId { get; set; }

    public Guid TenantId { get; set; }

    public FreizeitRolle Rolle { get; set; }

    public Nutzer? Nutzer { get; set; }

    public Freizeit? Freizeit { get; set; }
}

/// <summary>
/// Einladung mit Einmal-Link. Der Klartext-Token verlässt den Server genau
/// einmal — gespeichert wird nur sein Hash.
/// </summary>
public sealed class Einladung
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public required string Name { get; set; }

    public required string EMail { get; set; }

    public Einladungsziel Ziel { get; set; }

    public OrgRolle OrgRolle { get; set; }

    public FreizeitRolle? FreizeitRolle { get; set; }

    public Guid? FreizeitId { get; set; }

    public required string TokenHash { get; set; }

    public DateTimeOffset GueltigBis { get; set; }

    public DateTimeOffset? EingeloestAm { get; set; }

    public Guid ErstelltVon { get; set; }

    public Organisation? Organisation { get; set; }
}

/// <summary>
/// Erneuerungstoken hinter dem HttpOnly-Cookie. Wird bei jeder Verwendung
/// gedreht; ein zweites Einlösen desselben Tokens ist ein Diebstahlsignal.
/// </summary>
public sealed class Erneuerungstoken
{
    public Guid Id { get; set; }

    public Guid NutzerId { get; set; }

    public required string TokenHash { get; set; }

    public DateTimeOffset GueltigBis { get; set; }

    public DateTimeOffset? VerwendetAm { get; set; }

    public Nutzer? Nutzer { get; set; }
}
