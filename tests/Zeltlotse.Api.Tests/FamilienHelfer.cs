using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Gemeinsame Schritte der Familien-Tests (FR-3, FR-4): Kinder anlegen und ändern, zweiten Elternteil einladen, Profil
/// übergeben und annehmen über die echten Endpunkte und den Mail-Ordner.
/// </summary>
internal static class FamilienHelfer
{
    public const string Katrin = "katrin.muster@example.org";
    public const string Thomas = "thomas.muster@example.org";
    public const string Daniel = "daniel.probst@example.org";
    public const string PaulMail = "paul.muster@example.org";
    public const string EinladungsBetreff = "Einladung ins Familienkonto";
    public const string UebergabeBetreff = "Profil in Zeltlotse übernehmen";

    /// <summary>Volljähriges Geburtsdatum (die Uhr der Sitzungs-Factory läuft ab der echten Zeit, nach dem 14.03.2026).</summary>
    public static readonly DateOnly Volljaehrig = new(2008, 3, 14);

    public static readonly DateOnly EmmaGeboren = new(2017, 9, 2);

    public static Task<HttpResponseMessage> KindAnlegenAsync(HttpClient client, string? vorname, string? nachname, DateOnly? geburtsdatum) =>
        PostAsync(client, "/api/ich/familie/kinder", new KindHinzufuegen(vorname, nachname, geburtsdatum));

    /// <summary>Legt ein Kind an und liefert seine Kennung.</summary>
    public static async Task<Guid> KindAsync(HttpClient client, string vorname, DateOnly? geburtsdatum = null, string? nachname = "Muster")
    {
        using var antwort = await KindAnlegenAsync(client, vorname, nachname, geburtsdatum ?? EmmaGeboren);
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        return (await antwort.Content.ReadFromJsonAsync<KindHinzugefuegt>(Abbruch))!.PersonId;
    }

    /// <summary>Legt ein Kind an und setzt sein Geburtsdatum danach auf <see cref="Volljaehrig"/> (Anlegen geht nur unter 18).</summary>
    public static async Task<Guid> VolljaehrigesKindAsync(HttpClient client, string vorname)
    {
        var kind = await KindAsync(client, vorname, new DateOnly(2015, 3, 14));
        var version = await VersionAsync(client, kind);
        using var antwort = await KindPatchAsync(client, kind, $$"""{"version":{{version}},"alterWert":"2015-03-14","geburtsdatum":"{{Volljaehrig:yyyy-MM-dd}}"}""");
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return kind;
    }

    public static Task<HttpResponseMessage> KindPatchAsync(HttpClient client, Guid kind, string json) =>
        SendenAsync(client, HttpMethod.Patch, $"/api/ich/familie/kinder/{kind}", json);

    public static async Task<FamilieAntwort> FamilieAsync(HttpClient client)
    {
        using var antwort = await client.GetAsync("/api/ich/familie", Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return (await antwort.Content.ReadFromJsonAsync<FamilieAntwort>(Abbruch))!;
    }

    public static async Task<JsonElement> FamilieJsonAsync(HttpClient client)
    {
        using var antwort = await client.GetAsync("/api/ich/familie", Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    public static async Task<uint> VersionAsync(HttpClient client, Guid kind) =>
        (await FamilieAsync(client)).Kinder.Single(k => k.PersonId == kind).Version;

    public static Task<HttpResponseMessage> EinladenAsync(HttpClient client, string? mail) =>
        PostAsync(client, "/api/ich/familie/einladungen", new ElternteilEinladen(mail));

    public static string EinladungsToken(ZeltlotseApiFactory factory, string mail) =>
        factory.Mails.An(mail).Last(m => m.Betreff == EinladungsBetreff).Token;

    public static Task<HttpResponseMessage> EinladungAnnehmenAsync(HttpClient client, string? token) =>
        PostAsync(client, "/api/personen/familie/einladungen/annehmen", new FamilienEinladungAnnehmen(token));

    /// <summary>Lädt ein, meldet die Person an und nimmt an; liefert den angemeldeten Client des zweiten Elternteils.</summary>
    public static async Task<HttpClient> ZweiterElternteilAsync(ZeltlotseApiFactory factory, HttpClient eltern, string mail)
    {
        using (var einladen = await EinladenAsync(eltern, mail))
        {
            Assert.Equal(HttpStatusCode.Accepted, einladen.StatusCode);
        }

        var client = await AngemeldeterClientAsync(factory, mail);
        using var annehmen = await EinladungAnnehmenAsync(client, EinladungsToken(factory, mail));
        Assert.Equal(HttpStatusCode.OK, annehmen.StatusCode);
        return client;
    }

    public static Task<HttpResponseMessage> UebergebenAsync(HttpClient client, Guid kind, string? mail) =>
        PostAsync(client, $"/api/ich/familie/kinder/{kind}/uebergabe", new ProfilUebergeben(mail));

    public static string UebergabeToken(ZeltlotseApiFactory factory, string mail) =>
        factory.Mails.An(mail).Last(m => m.Betreff == UebergabeBetreff).Token;

    public static Task<HttpResponseMessage> UebernehmenAsync(HttpClient client, string? token) =>
        PostAsync(client, "/api/personen/uebergabe/annehmen", new ProfilUebernehmen(token));
}
