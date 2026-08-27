using Zeltlotse.Core.Freizeiten.Contracts;
using Zeltlotse.Core.Organisationen.Contracts;

namespace Zeltlotse.Core.Konten.Contracts;

/// <summary>Worauf sich eine Einladung bezieht.</summary>
public enum Einladungsziel
{
    Organisation = 0,
    Freizeit = 1,
}

public sealed record AnmeldungAnfrage(string EMail, string Kennwort);

/// <summary>
/// Antwort auf Anmeldung und Erneuerung. Das Erneuerungstoken steht bewusst
/// nicht darin — es reist ausschließlich als HttpOnly-Cookie.
/// </summary>
public sealed record AnmeldungAntwort(string Zugriffstoken, int GueltigSekunden);

public sealed record EinrichtungAnfrage(string Name, string EMail, string Kennwort);

public sealed record AngemeldeterNutzerDto(
    Guid Id,
    string Name,
    string EMail,
    bool IstGlobalAdmin,
    IReadOnlyList<OrganisationDto> Organisationen);

public sealed record EinladungAnlegen(
    string Name,
    string EMail,
    Einladungsziel Ziel,
    OrgRolle? OrgRolle,
    FreizeitRolle? FreizeitRolle,
    Guid? FreizeitId);

/// <summary>Der Klartext-Token existiert genau hier, genau einmal.</summary>
public sealed record EinladungErzeugtDto(
    Guid Id,
    string Name,
    string EMail,
    string Link,
    DateTimeOffset GueltigBis);

public sealed record OffeneEinladungDto(
    Guid Id,
    string Name,
    string EMail,
    string Beschreibung,
    DateTimeOffset GueltigBis);

public sealed record EinladungEinloesen(string Token, string Kennwort);

public sealed record EinladungVorschauDto(string Name, string EMail, string OrganisationName);

public sealed record KontoDto(
    Guid Id,
    string Name,
    string EMail,
    bool IstGlobalAdmin,
    bool Gesperrt,
    DateTimeOffset? LetzteAnmeldung);

public sealed record KennwortAendern(string Alt, string Neu);

/// <summary>
/// Einladungen erzeugen — der einzige Weg, auf dem andere Scheiben Menschen
/// ins System holen. Einlösen bleibt in der Konten-Scheibe.
/// </summary>
public interface IEinladungen
{
    Task<EinladungErzeugtDto> ErstellenAsync(
        Guid organisationId,
        Guid ersteller,
        EinladungAnlegen anfrage,
        CancellationToken abbruch);

    Task<IReadOnlyList<OffeneEinladungDto>> OffeneAsync(
        Guid organisationId,
        CancellationToken abbruch);
}

/// <summary>
/// Namen der Ansprüche im Zugriffstoken und die Lesehilfen dazu. Sie liegen
/// hier, weil jede Scheibe sie braucht — nicht nur die Konten-Scheibe.
/// </summary>
public static class Ansprueche
{
    public const string Organisationen = "zl:orgs";

    public const string GlobalAdmin = "zl:globaladmin";

    public static Guid NutzerId(this System.Security.Claims.ClaimsPrincipal angemeldet)
        => Guid.TryParse(
            angemeldet.FindFirst("sub")?.Value
                ?? angemeldet.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            out var id)
            ? id
            : Guid.Empty;

    public static bool IstGlobalAdmin(this System.Security.Claims.ClaimsPrincipal angemeldet)
        => angemeldet.HasClaim(GlobalAdmin, "1");
}
