using System.Net;
using System.Net.Http.Json;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Organisationen;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Gemeinsame Schritte der Organisations-Tests (FR-5): Factory mit Global Admin aus der Konfiguration, Organisation anlegen,
/// einladen und annehmen über die echten Endpunkte und den Mail-Ordner.
/// </summary>
internal static class OrgHelfer
{
    public const string Gerd = "gerd.betreiber@example.org";
    public const string Andrea = "andrea.beispiel@example.org";
    public const string Jonas = "jonas.huettner@example.org";
    public const string Lea = "lea.feuerstein@example.org";

    /// <summary>Sitzungs-Factory (<see cref="AnmeldeHelfer.Factory"/>) mit <see cref="Gerd"/> als Global Admin.</summary>
    public static ZeltlotseApiFactory Factory(string verbindung)
    {
        var factory = AnmeldeHelfer.Factory(verbindung);
        factory.Einstellungen["Zeltlotse:GlobalAdmins:0"] = Gerd;
        return factory;
    }

    /// <summary>Legt als Global Admin eine Organisation an und liefert ihre Kennung.</summary>
    public static async Task<Guid> AnlegenAsync(HttpClient globalAdmin, string name, string mail)
    {
        using var antwort = await PostAsync(globalAdmin, "/api/organisationen", new OrganisationAnlegen(name, mail));
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        return (await antwort.Content.ReadFromJsonAsync<OrganisationAngelegt>(Abbruch))!.Id;
    }

    /// <summary>Lädt als Org-Admin eine Mail-Adresse mit Rolle ein.</summary>
    public static async Task EinladenAsync(HttpClient orgAdmin, Guid orgId, string mail, OrgRolle rolle)
    {
        using var antwort = await PostAsync(orgAdmin, $"/api/organisationen/{orgId}/einladungen", new EinladungSchicken(mail, rolle));
        Assert.Equal(HttpStatusCode.Accepted, antwort.StatusCode);
    }

    /// <summary>Das Token aus der neuesten Einladungs-Mail an die Adresse.</summary>
    public static string Token(ZeltlotseApiFactory factory, string mail) =>
        factory.Mails.An(mail).Last(m => m.Betreff == "Einladung zu Zeltlotse").Token;

    public static Task<HttpResponseMessage> AnnehmenAsync(HttpClient client, string? token) =>
        PostAsync(client, "/api/organisationen/einladungen/annehmen", new EinladungAnnehmen(token));

    /// <summary>Meldet die eingeladene Person an und nimmt die neueste Einladung an; liefert den angemeldeten Client.</summary>
    public static async Task<HttpClient> AnnehmenAlsAsync(ZeltlotseApiFactory factory, string mail)
    {
        var token = Token(factory, mail);
        var client = await AngemeldeterClientAsync(factory, mail);
        using var antwort = await AnnehmenAsync(client, token);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return client;
    }

    /// <summary>Gerd legt die Organisation an, die Person der Mail-Adresse nimmt als Org-Admin an.</summary>
    public static async Task<(Guid OrgId, HttpClient OrgAdmin)> OrganisationMitOrgAdminAsync(
        ZeltlotseApiFactory factory, HttpClient gerd, string name, string mail)
    {
        var orgId = await AnlegenAsync(gerd, name, mail);
        return (orgId, await AnnehmenAlsAsync(factory, mail));
    }
}
