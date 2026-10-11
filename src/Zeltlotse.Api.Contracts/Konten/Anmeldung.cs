namespace Zeltlotse.Api.Contracts.Konten;

/// <summary>Körper von <c>POST /api/konten/login-link</c>: schickt Login-Link und Login-Code an die Mail-Adresse.</summary>
/// <param name="Mail">Mail-Adresse; Groß- und Kleinschreibung und Leerzeichen am Rand zählen nicht.</param>
public sealed record LoginLinkAnfordern(string? Mail);

/// <summary>
/// Antwort <c>202</c> von <c>POST /api/konten/login-link</c>, für bekannte und unbekannte Mail-Adressen gleich.
/// Mit der Kennung löst die installierte App den Login-Code ein.
/// </summary>
public sealed record LoginLinkAntwort(Guid AnfrageId);

/// <summary>Körper von <c>POST /api/konten/login-link/einloesen</c>: das Token aus dem Login-Link.</summary>
public sealed record LoginLinkEinloesen(string? Token);

/// <summary>Körper von <c>POST /api/konten/login-code/einloesen</c>: Kennung der Anfrage und der sechsstellige Code aus der Mail.</summary>
public sealed record LoginCodeEinloesen(Guid AnfrageId, string? Code);

/// <summary>Körper von <c>POST /api/konten/anmelden</c>: Anmeldung mit Mail-Adresse und Passwort.</summary>
public sealed record PasswortAnmeldung(string? Mail, string? Passwort);

/// <summary>Antwort von <c>GET /api/ich/konto</c>.</summary>
/// <param name="Mail">Die Mail-Adresse des Kontos.</param>
/// <param name="HatPasswort">Ob ein Passwort festgelegt ist.</param>
public sealed record KontoAntwort(string Mail, bool HatPasswort);

/// <summary>Körper von <c>PUT /api/ich/konto/passwort</c>: das neue Passwort, 10 bis 200 Zeichen.</summary>
public sealed record PasswortFestlegen(string? Passwort);
