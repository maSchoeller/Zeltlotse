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
    /// <summary>
    /// Die anonymen Endpunkte: Betrieb, die vier Anmelde-Endpunkte, die zwei Anmeldelink-Seiten (Story 2-1), die Gast-Anmeldung mit
    /// Gast-Link (Story 2-2) und der Freizeitpass über den Gast-Link (Story 2-4).
    /// </summary>
    private static readonly string[] Anonym =
    [
        "GET /api/betrieb/antiforgery",
        "GET /api/freizeiten/gast/{gastToken}",
        "GET /api/freizeiten/oeffentlich/{token}",
        "GET /api/freizeiten/oeffentlich/{token}/vertrauensseite",
        // Story 2-7: öffentliche Dokumente über den Anmeldelink und die Elterninfo über den Gast-Link.
        "GET /api/freizeiten/oeffentlich/{token}/dokumente/{dokumentId:guid}",
        "GET /api/freizeiten/gast/{gastToken}/anmeldungen/{anmeldungId:guid}/elterninfo",
        "GET /api/freizeiten/gast/{gastToken}/anmeldungen/{anmeldungId:guid}/elterninfo/{dokumentId:guid}",
        "POST /api/betrieb/fehler",
        "POST /api/freizeiten/oeffentlich/{token}/anmeldungen",
        "POST /api/personen/oeffentlich/gaeste",
        "POST /api/personen/oeffentlich/gaeste/{gastToken}/kinder",
        "GET /api/personen/oeffentlich/gaeste/{gastToken}/freizeitpass/{personId:guid}",
        "POST /api/personen/oeffentlich/gaeste/{gastToken}/freizeitpass/{personId:guid}",
        "POST /api/konten/anmelden",
        "POST /api/konten/login-code/einloesen",
        "POST /api/konten/login-link",
        "POST /api/konten/login-link/einloesen",
    ];

    /// <summary>Die personenbezogenen Endpunkte über Organisationen hinweg (AD-26), namentlich.</summary>
    private static readonly string[] Organisationsuebergreifend =
    [
        "GET /api/ich/anmeldungen",
        // Story 2-7: Elterninfo je Anmeldung (die Person selbst oder Eltern ihrer Familie).
        "GET /api/ich/anmeldungen/{anmeldungId:guid}/elterninfo",
        "GET /api/ich/anmeldungen/{anmeldungId:guid}/elterninfo/{dokumentId:guid}",
        "GET /api/ich/daten",
        "GET /api/ich/familie",
        "GET /api/ich/freizeiten",
        "GET /api/ich/freizeitpass/{personId:guid}",
        "GET /api/ich/konto",
        "GET /api/ich/nachweise",
        "GET /api/ich/organisationen",
        "GET /api/ich/organisationen/nachweis-bestaetigungen",
        "GET /api/ich/organisationen/namen",
        "GET /api/ich/profil",
        "GET /api/ich/zugriffsprotokoll",
        "PATCH /api/ich/familie/kinder/{personId:guid}",
        "PATCH /api/ich/freizeitpass/{personId:guid}/antworten/{kennung}",
        "PATCH /api/ich/nachweise/{nachweisId:guid}",
        "PATCH /api/ich/profil",
        "POST /api/ich/anmeldungen",
        "POST /api/ich/familie/einladungen",
        "POST /api/ich/familie/kinder",
        "POST /api/ich/familie/kinder/{personId:guid}/uebergabe",
        "POST /api/ich/freizeitpass/{personId:guid}/antworten/{kennung}/bestaetigen",
        "POST /api/ich/gastprofil/uebernehmen",
        "POST /api/ich/nachweise",
        "POST /api/ich/nachweise/einsichten/{einsichtId:guid}/freigeben",
        "POST /api/ich/nachweise/{nachweisId:guid}/datei",
        "POST /api/ich/profil",
        "POST /api/ich/profil/loeschen",
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
    public async Task Die_Anmeldelink_Endpunkte_sind_zugriffsbegrenzt_und_tragen_keine_freizeitId()
    {
        var endpunkte = await EndpunkteAsync();

        // Story 2-2: dazu der Gast-Link, das Absenden der Gast-Anmeldung und die zwei Gast-Endpunkte von Personen; Story 2-4: der
        // Freizeitpass über den Gast-Link (Lesen und Speichern).
        var oeffentlich = endpunkte.Where(e => e.Pfad.StartsWith("/api/freizeiten/oeffentlich/", StringComparison.Ordinal)
                                               || e.Pfad.StartsWith("/api/freizeiten/gast/", StringComparison.Ordinal)
                                               || e.Pfad.StartsWith("/api/personen/oeffentlich/", StringComparison.Ordinal)).ToList();
        // Story 2-7: dazu die öffentliche Datei eines Dokuments und die zwei Endpunkte der Elterninfo über den Gast-Link.
        Assert.Equal(11, oeffentlich.Count);
        Assert.All(oeffentlich, e =>
        {
            Assert.Contains(e.Name, Anonym);
            Assert.Contains(
                e.Endpunkt.Metadata.OfType<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>(),
                r => r.PolicyName == Zugriffsbegrenzung.Richtlinie);
            // Der Mandant kommt aus dem Token, nie aus einer Freizeit in der Route (AD-6).
            Assert.DoesNotContain(e.Endpunkt.RoutePattern.Parameters, p => p.Name == Rechtemerkmale.FreizeitRouteWert || p.Name == Rechtemerkmale.OrgRouteWert);
            Assert.Null(e.Endpunkt.Metadata.GetMetadata<NurFreizeitRolleMetadaten>());
        });
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
    public async Task Annehmen_der_Leitungs_Einladung_ist_angemeldet_und_zugriffsbegrenzt()
    {
        var endpunkte = await EndpunkteAsync();

        var annehmen = Assert.Single(endpunkte, e => e.Name == "POST /api/freizeiten/leitung/annehmen");
        Assert.Contains(
            annehmen.Endpunkt.Metadata.OfType<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>(),
            r => r.PolicyName == Zugriffsbegrenzung.Richtlinie);
        Assert.NotEmpty(annehmen.Endpunkt.Metadata.GetOrderedMetadata<IAuthorizeData>());
    }

    [Theory]
    [InlineData("POST /api/personen/familie/einladungen/annehmen")]
    [InlineData("POST /api/personen/uebergabe/annehmen")]
    [InlineData("POST /api/ich/gastprofil/uebernehmen")]
    [InlineData("POST /api/ich/anmeldungen")]
    public async Task Annehmen_von_Familien_Einladung_und_Profil_Uebergabe_ist_angemeldet_und_zugriffsbegrenzt(string name)
    {
        var endpunkte = await EndpunkteAsync();

        var annehmen = Assert.Single(endpunkte, e => e.Name == name);
        Assert.Contains(
            annehmen.Endpunkt.Metadata.OfType<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>(),
            r => r.PolicyName == Zugriffsbegrenzung.Richtlinie);
        Assert.NotEmpty(annehmen.Endpunkt.Metadata.GetOrderedMetadata<IAuthorizeData>());
    }

    [Fact]
    public async Task Nur_Profil_Uebergabe_Datenauskunft_und_Konto_loeschen_verlangen_die_erneute_Anmeldung()
    {
        var endpunkte = await EndpunkteAsync();

        // AD-25: vor Export, Konto-Löschung und Profil-Übergabe (Story 1-7: Auskunft und Löschen).
        Assert.Equal(
            ["GET /api/ich/daten", "POST /api/ich/familie/kinder/{personId:guid}/uebergabe", "POST /api/ich/profil/loeschen"],
            endpunkte.Where(e => e.Endpunkt.Metadata.GetMetadata<Zeltlotse.SharedKernel.Anmeldung.ErneuteAnmeldungMetadaten>() is not null)
                .Select(e => e.Name)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Jede_Freizeit_Route_traegt_NurFreizeitRolle_und_die_Guid_Einschraenkung_und_kein_Endpunkt_beide_Merkmale()
    {
        var endpunkte = await EndpunkteAsync();

        // Freizeit-Routen: freizeitId ohne orgId. Org-Routen mit freizeitId (Verwaltung) prüfen die Org-Rolle.
        var mitFreizeit = endpunkte.Where(e => e.Endpunkt.RoutePattern.Parameters.Any(p => p.Name == Rechtemerkmale.FreizeitRouteWert)).ToList();
        var freizeitRouten = mitFreizeit.Where(e => e.Endpunkt.RoutePattern.Parameters.All(p => p.Name != Rechtemerkmale.OrgRouteWert)).ToList();
        // Story 1-5: die sieben Prüf-Endpunkte der Leitung (Personen).
        const string pruefen = "/api/freizeiten/{freizeitId:guid}/personen/nachweise";
        Assert.Equal(
            new[]
            {
                "GET /api/freizeiten/{freizeitId:guid}",
                "GET /api/freizeiten/{freizeitId:guid}/kontext",
                // Story 1-6: Warnhinweise (Freizeiten) und Erinnern (Personen).
                "GET /api/freizeiten/{freizeitId:guid}/nachweis-warnungen",
                $"POST {pruefen}/erinnerungen",
                $"GET {pruefen}",
                $"GET {pruefen}/einsichten/{{einsichtId:guid}}",
                $"GET {pruefen}/einsichten/{{einsichtId:guid}}/nachweise/{{nachweisId:guid}}/datei",
                $"POST {pruefen}/anfragen",
                $"POST {pruefen}/einsichten/{{einsichtId:guid}}/nachweise/{{nachweisId:guid}}/ablehnen",
                $"POST {pruefen}/einsichten/{{einsichtId:guid}}/nachweise/{{nachweisId:guid}}/bestaetigen",
                $"POST {pruefen}/einsichten/{{einsichtId:guid}}/pruefen",
                // Story 2-1: Einstellungen der Freizeitseite und Anmeldelinks (Leitung).
                "GET /api/freizeiten/{freizeitId:guid}/freizeitseite",
                "PATCH /api/freizeiten/{freizeitId:guid}/freizeitseite",
                "POST /api/freizeiten/{freizeitId:guid}/anmeldelinks/{art}/erneuern",
                // Story 2-3: Anmeldungen der Leitung (Liste, Bestätigen, Absagen).
                "GET /api/freizeiten/{freizeitId:guid}/anmeldungen",
                "POST /api/freizeiten/{freizeitId:guid}/anmeldungen/{anmeldungId:guid}/bestaetigen",
                "POST /api/freizeiten/{freizeitId:guid}/anmeldungen/{anmeldungId:guid}/absagen",
                // Story 2-4: Fragen-Auswahl des Freizeitpasses (Leitung).
                "GET /api/freizeiten/{freizeitId:guid}/fragen",
                "PATCH /api/freizeiten/{freizeitId:guid}/fragen",
                // Story 2-5: Freizeitpass im Freizeit-Kontext (Personen), Personenliste und Team-Notizen (Freizeiten), Leitung und Team.
                "GET /api/freizeiten/{freizeitId:guid}/personen/freizeitpass/{personId:guid}",
                "GET /api/freizeiten/{freizeitId:guid}/personenliste",
                "GET /api/freizeiten/{freizeitId:guid}/anmeldungen/{anmeldungId:guid}/notizen",
                "POST /api/freizeiten/{freizeitId:guid}/anmeldungen/{anmeldungId:guid}/notizen",
                "DELETE /api/freizeiten/{freizeitId:guid}/anmeldungen/{anmeldungId:guid}/notizen/{notizId:guid}",
                // Story 2-6: Freizeitpass-Stand und Erinnern zum Freizeitpass (Personen, Leitung).
                "GET /api/freizeiten/{freizeitId:guid}/personen/freizeitpass-stand",
                "POST /api/freizeiten/{freizeitId:guid}/personen/freizeitpass/erinnerungen",
                // Story 2-7: Dokumente der Freizeit und Einbinden von Org-Dokumenten (Leitung und Team).
                "GET /api/freizeiten/{freizeitId:guid}/dokumente",
                "POST /api/freizeiten/{freizeitId:guid}/dokumente",
                "PATCH /api/freizeiten/{freizeitId:guid}/dokumente/{dokumentId:guid}",
                "DELETE /api/freizeiten/{freizeitId:guid}/dokumente/{dokumentId:guid}",
                "GET /api/freizeiten/{freizeitId:guid}/dokumente/{dokumentId:guid}/datei",
                "GET /api/freizeiten/{freizeitId:guid}/dokumente/org",
                "POST /api/freizeiten/{freizeitId:guid}/dokumente/einbindungen",
                // Story 2-8: Sicherheitskette (Sicherheit): lesen für Leitung und Team, einrichten nur Leitung.
                "GET /api/freizeiten/{freizeitId:guid}/sicherheit/sicherheitskette",
                "PATCH /api/freizeiten/{freizeitId:guid}/sicherheit/sicherheitskette",
                // Story 2-9: Schutzkonzept (Sicherheit): lesen und bestätigen für Leitung und Team, Dokument wählen nur Leitung.
                "GET /api/freizeiten/{freizeitId:guid}/sicherheit/schutzkonzept",
                "PATCH /api/freizeiten/{freizeitId:guid}/sicherheit/schutzkonzept",
                "POST /api/freizeiten/{freizeitId:guid}/sicherheit/schutzkonzept/gelesen",
            }.Order(StringComparer.Ordinal),
            freizeitRouten.Select(e => e.Name).Order(StringComparer.Ordinal));
        Assert.All(mitFreizeit, e => Assert.Contains("{freizeitId:guid}", e.Pfad, StringComparison.Ordinal));
        Assert.All(freizeitRouten, e => Assert.NotNull(e.Endpunkt.Metadata.GetMetadata<NurFreizeitRolleMetadaten>()));
        Assert.All(
            endpunkte.Where(e => e.Endpunkt.Metadata.GetMetadata<NurFreizeitRolleMetadaten>() is not null),
            e => Assert.Contains(e, freizeitRouten));
        Assert.DoesNotContain(
            endpunkte,
            e => e.Endpunkt.Metadata.GetMetadata<NurFreizeitRolleMetadaten>() is not null && e.Endpunkt.Metadata.GetMetadata<NurOrgRolleMetadaten>() is not null);
    }

    [Fact]
    public async Task Jede_Route_mit_orgId_traegt_NurOrgRolle_und_die_Guid_Einschraenkung()
    {
        var endpunkte = await EndpunkteAsync();

        var mitOrg = endpunkte.Where(e => e.Endpunkt.RoutePattern.Parameters.Any(p => p.Name == Rechtemerkmale.OrgRouteWert)).ToList();
        // Story 1-5: dazu die sieben Prüf-Endpunkte des Org-Admins und die zwei der Nachweis-Richtlinie; Story 1-7: die zwei der Löschfristen.
        // Story 2-7: die vier Endpunkte der Org-Dokumente.
        Assert.Equal(22, mitOrg.Count);
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
