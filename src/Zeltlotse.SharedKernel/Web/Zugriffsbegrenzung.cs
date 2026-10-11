using Microsoft.AspNetCore.Builder;

namespace Zeltlotse.SharedKernel.Web;

/// <summary>
/// Zugriffsbegrenzung (AD-11, AD-25) je Besucher-IP für Anmeldung, Zugangslinks und Gast-Anmeldung.
/// Der API-Host registriert die Richtlinie; Endpunkte melden sich mit <see cref="Zugriffsbegrenzt{TBuilder}"/> an.
/// </summary>
public static class Zugriffsbegrenzung
{
    public const string Richtlinie = "zugriffsbegrenzt";

    public static TBuilder Zugriffsbegrenzt<TBuilder>(this TBuilder endpunkt)
        where TBuilder : IEndpointConventionBuilder =>
        endpunkt.RequireRateLimiting(Richtlinie);
}
