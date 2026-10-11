using Zeltlotse.Api.Contracts.Gemeinsam;

namespace Zeltlotse.Api.Contracts.Organisationen;

/// <summary>Eine Zeile von <c>GET /api/organisationen</c> (Global Admin), nach Name sortiert.</summary>
/// <param name="Eingerichtet">Ob der erste Org-Admin seine Einladung angenommen hat.</param>
public sealed record OrganisationZeile(Guid Id, string Name, bool Eingerichtet);

/// <summary>Körper von <c>POST /api/organisationen</c>: Name (1–100 Zeichen) und Mail-Adresse des ersten Org-Admins.</summary>
public sealed record OrganisationAnlegen(string? Name, string? Mail);

/// <summary>Antwort <c>201</c> von <c>POST /api/organisationen</c>.</summary>
public sealed record OrganisationAngelegt(Guid Id);

/// <summary>Antwort von <c>GET /api/organisationen/{orgId}</c>.</summary>
/// <param name="Rollen">Die eigenen Rollen in der Organisation.</param>
/// <param name="Erlaubt">Erlaubte <see cref="Aktionsschluessel"/>; die Oberfläche entscheidet nur darüber (AD-29).</param>
public sealed record OrganisationAntwort(Guid Id, string Name, IReadOnlyList<OrgRolle> Rollen, IReadOnlyList<string> Erlaubt);

/// <summary>Antwort von <c>GET /api/organisationen/{orgId}/rollen</c> (Org-Admin).</summary>
/// <param name="Personen">Je Person und Rolle eine Zeile; Namen ohne Profil sind <c>null</c>.</param>
/// <param name="Einladungen">Nur offene, nicht abgelaufene Einladungen.</param>
public sealed record RollenAntwort(IReadOnlyList<RollenPerson> Personen, IReadOnlyList<OffeneEinladung> Einladungen);

/// <summary>Eine Person mit einer Rolle in der Organisation, ohne Mail-Adresse.</summary>
public sealed record RollenPerson(Guid PersonId, string? Vorname, string? Nachname, OrgRolle Rolle);

/// <summary>Eine offene Einladung der Organisation.</summary>
public sealed record OffeneEinladung(Guid Id, string Mail, OrgRolle Rolle, DateTimeOffset GueltigBis);

/// <summary>Körper von <c>POST /api/organisationen/{orgId}/einladungen</c>.</summary>
public sealed record EinladungSchicken(string? Mail, OrgRolle? Rolle);

/// <summary>Antwort <c>202</c> von <c>POST /api/organisationen/{orgId}/einladungen</c>.</summary>
public sealed record EinladungVerschickt(Guid Id);

/// <summary>Körper von <c>POST /api/organisationen/einladungen/annehmen</c>: das Token aus dem Einladungslink.</summary>
public sealed record EinladungAnnehmen(string? Token);

/// <summary>Antwort <c>200</c> von <c>POST /api/organisationen/einladungen/annehmen</c>.</summary>
public sealed record EinladungAngenommen(Guid OrganisationId);

/// <summary>Eine Zeile von <c>GET /api/ich/organisationen</c>: eine eigene Organisation mit den eigenen Rollen (AD-26).</summary>
public sealed record MeineOrganisation(Guid OrganisationId, string Name, IReadOnlyList<OrgRolle> Rollen);
