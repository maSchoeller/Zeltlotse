namespace Zeltlotse.Core.Freizeiten.Contracts;

public enum FreizeitStatus
{
    Offen = 0,
    Geschlossen = 1,
}

/// <summary>Rolle einer Person innerhalb einer einzelnen Freizeit.</summary>
public enum FreizeitRolle
{
    Mitarbeiter = 0,
    Leitung = 1,
}

public sealed record FreizeitDto(
    Guid Id,
    string Name,
    DateOnly? Beginn,
    DateOnly? Ende,
    string? Ort,
    FreizeitStatus Status,
    Guid OrganisationId,
    string OrganisationName,
    string OrganisationSlug,
    FreizeitRolle? EigeneRolle,
    bool DarfVerwalten);

public sealed record FreizeitTeamDto(
    Guid NutzerId,
    string Name,
    string EMail,
    FreizeitRolle Rolle);

/// <summary>Ein Mitglied der Organisation, das noch nicht im Team dieser Freizeit ist.</summary>
public sealed record KandidatDto(Guid NutzerId, string Name, string EMail);

public sealed record TeamZuordnung(Guid NutzerId, FreizeitRolle Rolle);

public sealed record PapierkorbEintragDto(
    Guid Id,
    string Name,
    DateTimeOffset GeloeschtAm,
    int VerbleibendeTage);

public sealed record FreizeitAnlegen(
    string Name,
    DateOnly? Beginn,
    DateOnly? Ende,
    string? Ort);

public sealed record FreizeitAendern(
    string Name,
    DateOnly? Beginn,
    DateOnly? Ende,
    string? Ort,
    FreizeitStatus Status);
