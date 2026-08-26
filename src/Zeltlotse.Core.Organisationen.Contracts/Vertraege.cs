namespace Zeltlotse.Core.Organisationen.Contracts;

/// <summary>Rolle einer Person innerhalb einer Organisation.</summary>
public enum OrgRolle
{
    /// <summary>Sieht die Organisation, mehr nicht.</summary>
    Mitglied = 0,

    /// <summary>Verwaltet Mitglieder, Freizeiten und Papierkorb.</summary>
    OrgAdmin = 1,
}

public sealed record OrganisationDto(
    Guid Id,
    string Name,
    string Slug,
    OrgRolle? EigeneRolle,
    DateTimeOffset? LoeschungBeantragtAm);

/// <summary>Sicht des Betreibers auf eine Organisation — ohne Inhalte.</summary>
public sealed record OrganisationVerwaltungDto(
    Guid Id,
    string Name,
    string Slug,
    int AnzahlMitglieder,
    string? Organisationsleitung,
    DateTimeOffset? LoeschungBeantragtAm,
    DateTimeOffset? GeloeschtAm);

public sealed record MitgliedDto(
    Guid NutzerId,
    string EMail,
    OrgRolle Rolle,
    DateTimeOffset SeitAm);

public sealed record OrganisationAnlegen(string Name);

public sealed record NamensvorschlagDto(string Slug);
