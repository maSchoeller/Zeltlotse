using System.Net;
using System.Net.Http.Json;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Gemeinsame Schritte der Freizeit-Tests (FR-6): Freizeit anlegen, Leitung einladen und annehmen über die echten Endpunkte
/// und den Mail-Ordner.
/// </summary>
internal static class FreizeitHelfer
{
    public const string Miriam = "miriam.zeltner@example.org";
    public const string Tobias = "tobias.wanderer@example.org";
    public const string Betreff = "Einladung als Leitung in Zeltlotse";

    public static readonly DateOnly Erster = new(2027, 8, 2);
    public static readonly DateOnly Letzter = new(2027, 8, 13);

    /// <summary>Legt als Org-Admin eine Freizeit an und liefert ihre Kennung.</summary>
    public static async Task<Guid> AnlegenAsync(HttpClient orgAdmin, Guid orgId, string name = "Zeltlager Sommer 2027", DateOnly? erster = null, DateOnly? letzter = null)
    {
        using var antwort = await PostAsync(orgAdmin, $"/api/organisationen/{orgId}/freizeiten", new FreizeitAnlegen(name, erster ?? Erster, letzter ?? Letzter));
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        return (await antwort.Content.ReadFromJsonAsync<FreizeitAngelegt>(Abbruch))!.Id;
    }

    /// <summary>Lädt als Org-Admin eine Mail-Adresse als Leitung ein.</summary>
    public static async Task EinladenAsync(HttpClient orgAdmin, Guid orgId, Guid freizeitId, string mail)
    {
        using var antwort = await PostAsync(orgAdmin, $"/api/organisationen/{orgId}/freizeiten/{freizeitId}/leitung/einladungen", new LeitungEinladen(mail));
        Assert.Equal(HttpStatusCode.Accepted, antwort.StatusCode);
    }

    /// <summary>Das Token aus der neuesten Leitungs-Einladung an die Adresse.</summary>
    public static string LeitungsToken(ZeltlotseApiFactory factory, string mail) =>
        factory.Mails.An(mail).Last(m => m.Betreff == Betreff).Token;

    public static Task<HttpResponseMessage> AnnehmenAsync(HttpClient client, string? token) =>
        PostAsync(client, "/api/freizeiten/leitung/annehmen", new LeitungAnnehmen(token));

    /// <summary>Nimmt mit dem angemeldeten Client die neueste Leitungs-Einladung an seine Mail-Adresse an.</summary>
    public static async Task AnnehmenMitAsync(ZeltlotseApiFactory factory, HttpClient client, string mail)
    {
        using var antwort = await AnnehmenAsync(client, LeitungsToken(factory, mail));
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
    }

    /// <summary>Lädt ein, meldet die Person an und nimmt an; liefert den angemeldeten Client der neuen Leitung.</summary>
    public static async Task<HttpClient> LeitungAsync(ZeltlotseApiFactory factory, HttpClient orgAdmin, Guid orgId, Guid freizeitId, string mail)
    {
        await EinladenAsync(orgAdmin, orgId, freizeitId, mail);
        var client = await AngemeldeterClientAsync(factory, mail);
        await AnnehmenMitAsync(factory, client, mail);
        return client;
    }

    /// <summary>Setzt Vor- und Nachname im eigenen Profil (über die echten Profil-Endpunkte).</summary>
    public static async Task NameSetzenAsync(HttpClient client, string vorname, string nachname)
    {
        using var profil = await PostAsync(client, "/api/ich/profil");
        var version = (await profil.Content.ReadFromJsonAsync<ProfilAntwort>(Abbruch))!.Version;
        using (var erstes = await SendenAsync(client, HttpMethod.Patch, "/api/ich/profil", $$"""{"version":{{version}},"alterWert":null,"vorname":"{{vorname}}"}"""))
        {
            Assert.Equal(HttpStatusCode.OK, erstes.StatusCode);
            version = (await JsonAsync(erstes)).GetProperty("version").GetUInt32();
        }

        using var zweites = await SendenAsync(client, HttpMethod.Patch, "/api/ich/profil", $$"""{"version":{{version}},"alterWert":null,"nachname":"{{nachname}}"}""");
        Assert.Equal(HttpStatusCode.OK, zweites.StatusCode);
    }

    /// <summary>Heute in Europe/Berlin nach der Uhr der Factory.</summary>
    public static DateOnly Heute(ZeltlotseApiFactory factory) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(factory.Uhr.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")).DateTime);
}
