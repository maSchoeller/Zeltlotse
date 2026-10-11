using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.SharedKernel.Rechte;
using Zeltlotse.SharedKernel.Web;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Autorisierung aller <c>/api</c>-Endpunkte (AD-12, AD-26): Jeder verlangt eine Sitzung, außer der ausdrücklich anonymen
/// Liste; jeder unter <c>/api/ich/</c> trägt <c>.Organisationsuebergreifend()</c> und steht namentlich in der Liste.
/// Neue Endpunkte müssen hier bewusst eingetragen werden.
/// </summary>
public sealed class EndpunktListeTests(PostgresContainer postgres)
{
    /// <summary>Die anonymen Endpunkte: Betrieb und die vier Anmelde-Endpunkte.</summary>
    private static readonly string[] Anonym =
    [
        "GET /api/betrieb/antiforgery",
        "POST /api/betrieb/fehler",
        "POST /api/konten/anmelden",
        "POST /api/konten/login-code/einloesen",
        "POST /api/konten/login-link",
        "POST /api/konten/login-link/einloesen",
    ];

    /// <summary>Die personenbezogenen Endpunkte über Organisationen hinweg (AD-26), namentlich.</summary>
    private static readonly string[] Organisationsuebergreifend =
    [
        "GET /api/ich/konto",
        "GET /api/ich/organisationen",
        "GET /api/ich/profil",
        "PATCH /api/ich/profil",
        "POST /api/ich/profil",
        "PUT /api/ich/konto/passwort",
    ];

    [Fact]
    public async Task Nur_die_anonyme_Liste_kommt_ohne_Sitzung_aus()
    {
        var endpunkte = await EndpunkteAsync();

        var anonym = endpunkte.Where(e => e.Endpunkt.Metadata.GetMetadata<IAllowAnonymous>() is not null).Select(e => e.Name).Order(StringComparer.Ordinal);
        Assert.Equal(Anonym.Order(StringComparer.Ordinal), anonym);
        Assert.All(
            endpunkte.Where(e => !Anonym.Contains(e.Name)),
            e => Assert.NotEmpty(e.Endpunkt.Metadata.GetOrderedMetadata<IAuthorizeData>()));
    }

    [Fact]
    public async Task Die_Anmelde_Endpunkte_sind_zugriffsbegrenzt()
    {
        var endpunkte = await EndpunkteAsync();

        Assert.All(
            endpunkte.Where(e => e.Name.StartsWith("POST /api/konten/", StringComparison.Ordinal) && Anonym.Contains(e.Name)),
            e => Assert.Contains(
                e.Endpunkt.Metadata.OfType<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>(),
                r => r.PolicyName == Zugriffsbegrenzung.Richtlinie));
        Assert.Equal(4, endpunkte.Count(e => e.Name.StartsWith("POST /api/konten/", StringComparison.Ordinal) && Anonym.Contains(e.Name)));
    }

    [Fact]
    public async Task Annehmen_der_Org_Einladung_ist_angemeldet_und_zugriffsbegrenzt()
    {
        var endpunkte = await EndpunkteAsync();

        var annehmen = Assert.Single(endpunkte, e => e.Name == "POST /api/organisationen/einladungen/annehmen");
        Assert.Contains(
            annehmen.Endpunkt.Metadata.OfType<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>(),
            r => r.PolicyName == Zugriffsbegrenzung.Richtlinie);
        Assert.NotEmpty(annehmen.Endpunkt.Metadata.GetOrderedMetadata<IAuthorizeData>());
    }

    [Fact]
    public async Task Jede_Route_mit_orgId_traegt_NurOrgRolle_und_die_Guid_Einschraenkung()
    {
        var endpunkte = await EndpunkteAsync();

        var mitOrg = endpunkte.Where(e => e.Endpunkt.RoutePattern.Parameters.Any(p => p.Name == Rechtemerkmale.OrgRouteWert)).ToList();
        Assert.Equal(3, mitOrg.Count);
        Assert.All(mitOrg, e =>
        {
            Assert.NotNull(e.Endpunkt.Metadata.GetMetadata<NurOrgRolleMetadaten>());
            Assert.Contains("{orgId:guid}", e.Pfad, StringComparison.Ordinal);
        });
        Assert.All(
            endpunkte.Where(e => e.Endpunkt.Metadata.GetMetadata<NurOrgRolleMetadaten>() is not null),
            e => Assert.Contains(e, mitOrg));
        Assert.Equal(
            ["GET /api/organisationen", "POST /api/organisationen"],
            endpunkte.Where(e => e.Endpunkt.Metadata.GetMetadata<NurGlobalAdminMetadaten>() is not null).Select(e => e.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Endpunkte_unter_api_ich_sind_namentlich_bekannt_und_organisationsuebergreifend_markiert()
    {
        var endpunkte = await EndpunkteAsync();

        var markiert = endpunkte.Where(e => e.Endpunkt.Metadata.GetMetadata<OrganisationsuebergreifendMetadaten>() is not null).Select(e => e.Name);
        var unterIch = endpunkte.Where(e => e.Pfad.StartsWith("/api/ich/", StringComparison.Ordinal) || e.Pfad == "/api/ich").Select(e => e.Name);
        Assert.Equal(Organisationsuebergreifend.Order(StringComparer.Ordinal), markiert.Order(StringComparer.Ordinal));
        Assert.Equal(Organisationsuebergreifend.Order(StringComparer.Ordinal), unterIch.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Jeder_angemeldete_Endpunkt_antwortet_ohne_Sitzung_mit_401_ProblemDetails()
    {
        await using var factory = Factory(await postgres.NeueDatenbankAsync());
        using var client = factory.CreateClient();
        var endpunkte = Endpunkte(factory).Where(e => !Anonym.Contains(e.Name)).ToList();
        Assert.NotEmpty(endpunkte);

        foreach (var (name, methode, pfad, _) in endpunkte)
        {
            // Routenwerte (z. B. {orgId:guid}) bekommen eine Kennung, damit die Route passt.
            var aufruf = System.Text.RegularExpressions.Regex.Replace(pfad, @"\{[^}]+\}", Guid.CreateVersion7().ToString());
            using var antwort = await SendenAsync(client, new HttpMethod(methode), aufruf, methode == "GET" ? null : "{}");

            Assert.True(antwort.StatusCode == HttpStatusCode.Unauthorized, $"{name}: {antwort.StatusCode}");
            Assert.Equal("application/problem+json", antwort.Content.Headers.ContentType?.MediaType);
            Assert.Null(antwort.Headers.Location);
        }
    }

    private async Task<List<(string Name, string Methode, string Pfad, RouteEndpoint Endpunkt)>> EndpunkteAsync()
    {
        var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync());
        await using (factory)
        {
            _ = factory.CreateClient();
            return Endpunkte(factory);
        }
    }

    private static List<(string Name, string Methode, string Pfad, RouteEndpoint Endpunkt)> Endpunkte(ZeltlotseApiFactory factory) =>
    [
        .. factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Select(e => (Endpunkt: e, Pfad: "/" + (e.RoutePattern.RawText ?? "").Trim('/')))
            .Where(e => e.Pfad == "/api" || (e.Pfad.StartsWith("/api/", StringComparison.Ordinal) && !e.Pfad.StartsWith("/api/test/", StringComparison.Ordinal)))
            .SelectMany(e => (e.Endpunkt.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.IHttpMethodMetadata>()?.HttpMethods ?? ["*"])
                .Select(m => ($"{m} {e.Pfad}", m, e.Pfad, e.Endpunkt))),
    ];
}
