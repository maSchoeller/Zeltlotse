using Zeltlotse.Api.Contracts.Gemeinsam;

namespace Zeltlotse.Api.Contracts.Freizeiten;

/// <summary>Antwort von <c>GET /api/organisationen/{orgId}/freizeiten</c> (Org-Admin, Org-Überblick).</summary>
/// <param name="Freizeiten">Nach erstem Tag absteigend.</param>
/// <param name="Erlaubt">Erlaubte <see cref="Aktionsschluessel"/>; die Oberfläche entscheidet nur darüber (AD-29).</param>
public sealed record FreizeitenListe(IReadOnlyList<FreizeitZeile> Freizeiten, IReadOnlyList<string> Erlaubt);

/// <summary>Eine Freizeit in der Liste der Organisation: nur Name, Zeitraum und Phase, keine Inhalte (AD-12).</summary>
public sealed record FreizeitZeile(Guid Id, string Name, DateOnly ErsterTag, DateOnly LetzterTag, Phase Phase);

/// <summary>Körper von <c>POST /api/organisationen/{orgId}/freizeiten</c>: Name (1–100 Zeichen) und Zeitraum.</summary>
public sealed record FreizeitAnlegen(string? Name, DateOnly? ErsterTag, DateOnly? LetzterTag);

/// <summary>Antwort <c>201</c> von <c>POST /api/organisationen/{orgId}/freizeiten</c>.</summary>
public sealed record FreizeitAngelegt(Guid Id);

/// <summary>Antwort von <c>GET /api/organisationen/{orgId}/freizeiten/{freizeitId}</c> (Org-Admin): Verwaltung der Freizeit.</summary>
/// <param name="Leitung">Die Leitung nur mit Namen, ohne Mail-Adressen.</param>
/// <param name="Einladungen">Nur offene, nicht abgelaufene Leitungs-Einladungen.</param>
public sealed record FreizeitVerwaltung(
    Guid Id,
    string Name,
    DateOnly ErsterTag,
    DateOnly LetzterTag,
    Phase Phase,
    IReadOnlyList<LeitungsPerson> Leitung,
    IReadOnlyList<OffeneLeitungsEinladung> Einladungen);

/// <summary>Eine Person der Leitung; Namen ohne Profil sind <c>null</c>.</summary>
public sealed record LeitungsPerson(Guid PersonId, string? Vorname, string? Nachname);

/// <summary>Eine offene Leitungs-Einladung.</summary>
public sealed record OffeneLeitungsEinladung(Guid Id, string Mail, DateTimeOffset GueltigBis);

/// <summary>Körper von <c>POST /api/organisationen/{orgId}/freizeiten/{freizeitId}/leitung/einladungen</c>.</summary>
public sealed record LeitungEinladen(string? Mail);

/// <summary>Antwort <c>202</c> von <c>POST /api/organisationen/{orgId}/freizeiten/{freizeitId}/leitung/einladungen</c>.</summary>
public sealed record LeitungEingeladen(Guid Id);

/// <summary>Körper von <c>POST /api/freizeiten/leitung/annehmen</c>: das Token aus dem Einladungslink.</summary>
public sealed record LeitungAnnehmen(string? Token);

/// <summary>Antwort <c>200</c> von <c>POST /api/freizeiten/leitung/annehmen</c>.</summary>
public sealed record LeitungAngenommen(Guid FreizeitId);

/// <summary>Antwort von <c>GET /api/freizeiten/{freizeitId}</c> (Leitung, Team): die Startseite der Freizeit.</summary>
/// <param name="FreigabeBis">Letzter Tag des Freigabefensters (letzter Freizeittag plus Intervall), als Tag angezeigt.</param>
/// <param name="Leitung">Die Leitung nur mit Namen.</param>
public sealed record FreizeitAntwort(
    Guid Id,
    string Name,
    DateOnly ErsterTag,
    DateOnly LetzterTag,
    Phase Phase,
    DateOnly FreigabeBis,
    IReadOnlyList<LeitungsName> Leitung);

/// <summary>Vor- und Nachname einer Person der Leitung.</summary>
public sealed record LeitungsName(string? Vorname, string? Nachname);

/// <summary>Eine Zeile von <c>GET /api/ich/freizeiten</c> (nach erstem Tag absteigend): eine eigene Freizeit mit den eigenen Rollen (AD-26).</summary>
public sealed record MeineFreizeit(Guid FreizeitId, string Name, DateOnly ErsterTag, DateOnly LetzterTag, Phase Phase, IReadOnlyList<FreizeitRolle> Rollen);
