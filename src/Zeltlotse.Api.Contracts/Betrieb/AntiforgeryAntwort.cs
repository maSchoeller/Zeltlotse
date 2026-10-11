namespace Zeltlotse.Api.Contracts.Betrieb;

/// <summary>Antwort von <c>GET /api/betrieb/antiforgery</c>; das passende Cookie setzt der Server. Nach der Anmeldung neu holen.</summary>
public sealed record AntiforgeryAntwort(string Token);
