using Microsoft.AspNetCore.Builder;

namespace Zeltlotse.SharedKernel.Rechte;

/// <summary>
/// Endpunkt-Merkmale der zentralen Rechteprüfung im API-Host (AD-6, AD-12). Der Host prüft sie nach Routing und
/// Authentifizierung, vor der Modellbindung:
/// <list type="bullet">
/// <item>Global Admin fehlt → <c>403</c>.</item>
/// <item>Keine Rolle in der Organisation aus dem Routenwert <see cref="OrgRouteWert"/> → <c>404</c>; Rolle vorhanden, aber
/// nicht die verlangte → <c>403</c>.</item>
/// <item>Freizeit aus dem Routenwert <see cref="FreizeitRouteWert"/> unbekannt oder keine eigene Freizeit-Rolle → <c>404</c>;
/// Rolle vorhanden, aber nicht die verlangte → <c>403</c>. Der Mandant ist die Organisation der Freizeit.</item>
/// </list>
/// Org- und Freizeit-Merkmal an demselben Endpunkt sind verboten. Story 3-7: <see cref="GehoertZuSchalter{TBuilder}"/> verlangt
/// <see cref="NurFreizeitRolle{TBuilder}"/>; ist der Schalter für die Freizeit aus, antwortet der Endpunkt nach bestandener Rollenprüfung
/// mit <c>404</c> (AD-20). Story 6-4: <see cref="NurQrGast{TBuilder}"/> steht immer allein: Der Endpunkt ist für die normale Sitzung anonym und
/// verlangt eine gültige Gast-Sitzung des QR-Teilnehmerzugangs; sonst <c>404</c>, nie <c>401</c>.
/// </summary>
public static class Rechtemerkmale
{
    /// <summary>Name des Routenwerts mit der Organisation; Routen schreiben ihn als <c>{orgId:guid}</c>.</summary>
    public const string OrgRouteWert = "orgId";

    /// <summary>Name des Routenwerts mit der Freizeit; Routen schreiben ihn als <c>{freizeitId:guid}</c>.</summary>
    public const string FreizeitRouteWert = "freizeitId";

    /// <summary>Nur für Global Admins.</summary>
    public static TBuilder NurGlobalAdmin<TBuilder>(this TBuilder endpunkt)
        where TBuilder : IEndpointConventionBuilder =>
        endpunkt.WithMetadata(NurGlobalAdminMetadaten.Instanz);

    /// <summary>Nur für Personen mit einer der Rollen in der Organisation aus der Route.</summary>
    public static TBuilder NurOrgRolle<TBuilder>(this TBuilder endpunkt, params OrgRolle[] rollen)
        where TBuilder : IEndpointConventionBuilder
    {
        if (rollen.Length == 0)
        {
            throw new ArgumentException("Mindestens eine Rolle.", nameof(rollen));
        }

        return endpunkt.WithMetadata(new NurOrgRolleMetadaten(rollen));
    }

    /// <summary>Nur für Personen mit einer der Rollen in der Freizeit aus der Route; Org-Rollen zählen nicht (AD-12).</summary>
    public static TBuilder NurFreizeitRolle<TBuilder>(this TBuilder endpunkt, params FreizeitRolle[] rollen)
        where TBuilder : IEndpointConventionBuilder
    {
        if (rollen.Length == 0)
        {
            throw new ArgumentException("Mindestens eine Rolle.", nameof(rollen));
        }

        return endpunkt.WithMetadata(new NurFreizeitRolleMetadaten(rollen));
    }

    /// <summary>
    /// Der Endpunkt gehört zum Funktions-Schalter (AD-20): Ist er für die Freizeit aus der Route aus, antwortet er mit <c>404</c>. Nur
    /// zusammen mit <see cref="NurFreizeitRolle{TBuilder}"/>; ohne Rolle bleibt es <c>404</c>, mit falscher Rolle <c>403</c>.
    /// </summary>
    public static TBuilder GehoertZuSchalter<TBuilder>(this TBuilder endpunkt, FunktionsSchalter schalter)
        where TBuilder : IEndpointConventionBuilder =>
        endpunkt.WithMetadata(new GehoertZuSchalterMetadaten(schalter));

    /// <summary>
    /// Story 6-4 (AD-12): Nur für den QR-Gast einer Freizeit. Der Endpunkt verlangt keine Sitzung (für sie ist er anonym); der Host prüft stattdessen das
    /// Gast-Cookie, setzt den Mandanten und füllt <see cref="IQrGast"/>. Fehlt die Gast-Sitzung oder gilt sie nicht (mehr), antwortet er mit <c>404</c>.
    /// Zusammen mit einem anderen Rechtemerkmal ist das ein Konfigurationsfehler.
    /// </summary>
    public static TBuilder NurQrGast<TBuilder>(this TBuilder endpunkt)
        where TBuilder : IEndpointConventionBuilder =>
        endpunkt.AllowAnonymous().WithMetadata(NurQrGastMetadaten.Instanz);
}

/// <summary>Story 6-4: Merkmal: Der Endpunkt verlangt eine gültige Gast-Sitzung des QR-Teilnehmerzugangs.</summary>
public sealed class NurQrGastMetadaten
{
    public static NurQrGastMetadaten Instanz { get; } = new();

    private NurQrGastMetadaten()
    {
    }
}

/// <summary>Merkmal: Der Endpunkt verlangt einen Global Admin.</summary>
public sealed class NurGlobalAdminMetadaten
{
    public static NurGlobalAdminMetadaten Instanz { get; } = new();

    private NurGlobalAdminMetadaten()
    {
    }
}

/// <summary>Merkmal: Der Endpunkt verlangt eine der <see cref="Rollen"/> in der Organisation der Route.</summary>
public sealed class NurOrgRolleMetadaten(IReadOnlyList<OrgRolle> rollen)
{
    public IReadOnlyList<OrgRolle> Rollen { get; } = rollen;
}

/// <summary>Merkmal: Der Endpunkt verlangt eine der <see cref="Rollen"/> in der Freizeit der Route.</summary>
public sealed class NurFreizeitRolleMetadaten(IReadOnlyList<FreizeitRolle> rollen)
{
    public IReadOnlyList<FreizeitRolle> Rollen { get; } = rollen;
}

/// <summary>Merkmal: Der Endpunkt gehört zum Funktions-Schalter <see cref="Schalter"/> (AD-20).</summary>
public sealed class GehoertZuSchalterMetadaten(FunktionsSchalter schalter)
{
    public FunktionsSchalter Schalter { get; } = schalter;
}
