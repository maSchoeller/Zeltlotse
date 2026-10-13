using System.Net;
using System.Text.Json;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.FreizeitHelfer;
using static Zeltlotse.Api.Tests.OrgHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Gemeinsamer Aufbau der Tests zu Story 2-5 (Freigabe, Freizeitpässe, Team-Notizen) über die echten Endpunkte: Organisation A (Andrea,
/// Org-Admin ohne Rolle in der Freizeit) mit Freizeit X, Organisation B (Lea) mit Freizeit Z; Miriam ist Leitung beider. Tobias meldet
/// sich in X als Mitarbeitender an und wird bestätigt (Team, auch in der Rollenliste von Personen). Katrin hat Emma und Mia im
/// Familienkonto. Miriam fragt in X für Teilnehmende Allergien, Notfallkontakte und Schwimmen ab, in Z Allergien. Katrin hat für Emma
/// Allergien (Milch), Krankheiten (nicht abgefragt) und Notfallkontakte beantwortet. Angemeldet ist noch niemand außer Tobias.
/// </summary>
internal sealed class FreizeitpassSzenario : IAsyncDisposable
{
    public const string Kapf = "Zeltlager Kapf 2027";
    public const string NotfallkontaktEmma = "Katrin Muster (Mutter), 0123 456780";

    /// <summary>Letzter Tag 13.08.2027 plus 30 Tage Freigabe-Intervall: 13.09.2027 00:00 Europe/Berlin.</summary>
    public static readonly DateTimeOffset FreigabeEnde = new(2027, 9, 12, 22, 0, 0, TimeSpan.Zero);

    private readonly List<HttpClient> _clients = [];

    private FreizeitpassSzenario(string verbindung)
    {
        Verbindung = verbindung;
        Factory = OrgHelfer.Factory(verbindung);
    }

    public string Verbindung { get; }

    public ZeltlotseApiFactory Factory { get; }

    public Guid A { get; private set; }

    /// <summary>Story 2-7: Organisation B (Lea Org-Admin) mit Freizeit Z.</summary>
    public Guid B { get; private set; }

    /// <summary>Story 2-7: Bucket des S3-Containers, wenn das Szenario mit Dateispeicher aufgebaut ist.</summary>
    public string? Bucket { get; private set; }

    public Guid X { get; private set; }

    public Guid Z { get; private set; }

    public HttpClient Gerd { get; private set; } = null!;

    public HttpClient Andrea { get; private set; } = null!;

    public HttpClient Lea { get; private set; } = null!;

    public HttpClient Miriam { get; private set; } = null!;

    public HttpClient Tobias { get; private set; } = null!;

    public HttpClient Katrin { get; private set; } = null!;

    public HttpClient Anonym { get; private set; } = null!;

    public Guid MiriamId { get; private set; }

    public Guid TobiasId { get; private set; }

    public Guid KatrinId { get; private set; }

    public Guid Emma { get; private set; }

    public Guid Mia { get; private set; }

    public Guid AnmeldungTobias { get; private set; }

    public string TeilnehmendeX { get; private set; } = null!;

    public string TeilnehmendeZ { get; private set; } = null!;

    public static async Task<FreizeitpassSzenario> AufbauenAsync(PostgresContainer postgres, S3Container? s3 = null)
    {
        var szenario = new FreizeitpassSzenario(await postgres.NeueDatenbankAsync());
        if (s3 is not null)
        {
            // Story 2-7: Dokumente brauchen den Dateispeicher (S3-Container mit eigenem Bucket).
            szenario.Bucket = S3Container.NeuerBucket();
            foreach (var (schluessel, wert) in s3.Einstellungen(szenario.Bucket))
            {
                szenario.Factory.Einstellungen[schluessel] = wert;
            }
        }

        await szenario.InitAsync();
        return szenario;
    }

    private async Task InitAsync()
    {
        Gerd = Merken(await AngemeldeterClientAsync(Factory, OrgHelfer.Gerd));
        (A, Andrea) = await OrganisationMitOrgAdminAsync(Factory, Gerd, "CVJM Musterstadt", OrgHelfer.Andrea);
        Merken(Andrea);
        Guid b;
        (b, Lea) = await OrganisationMitOrgAdminAsync(Factory, Gerd, "EJW Beispielhausen", OrgHelfer.Lea);
        B = b;
        Merken(Lea);
        X = await AnlegenAsync(Andrea, A, Kapf);
        Z = await AnlegenAsync(Lea, b, "Z", new DateOnly(2027, 9, 1), new DateOnly(2027, 9, 3));
        Miriam = Merken(await LeitungAsync(Factory, Andrea, A, X, FreizeitHelfer.Miriam));
        await NameSetzenAsync(Miriam, "Miriam", "Zeltner");
        await FreizeitHelfer.EinladenAsync(Lea, b, Z, FreizeitHelfer.Miriam);
        await AnnehmenMitAsync(Factory, Miriam, FreizeitHelfer.Miriam);
        MiriamId = await PersonIdAsync(Verbindung, FreizeitHelfer.Miriam);
        await FragenAsync(X, "allergien,notfallkontakte,schwimmen");
        await FragenAsync(Z, "allergien");
        var linksX = await AnmeldelinksAsync(X);
        TeilnehmendeX = TokenAus(linksX["Teilnehmende"]);
        TeilnehmendeZ = TokenAus((await AnmeldelinksAsync(Z))["Teilnehmende"]);

        Katrin = Merken(await AngemeldeterClientAsync(Factory, FamilienHelfer.Katrin));
        await NameSetzenAsync(Katrin, "Katrin", "Muster");
        KatrinId = await PersonIdAsync(Verbindung, FamilienHelfer.Katrin);
        Emma = await FamilienHelfer.KindAsync(Katrin, "Emma");
        Mia = await FamilienHelfer.KindAsync(Katrin, "Mia", new DateOnly(2016, 5, 21));
        await AntwortAsync(Emma, "allergien", "milch");
        await AntwortAsync(Emma, "krankheiten", "Asthma");
        await AntwortAsync(Emma, "notfallkontakte", NotfallkontaktEmma);

        Tobias = Merken(await AngemeldeterClientAsync(Factory, FreizeitHelfer.Tobias));
        await NameSetzenAsync(Tobias, "Tobias", "Wanderer");
        TobiasId = await PersonIdAsync(Verbindung, FreizeitHelfer.Tobias);
        AnmeldungTobias = await AnmeldenAsync(Tobias, TokenAus(linksX["Mitarbeitende"]), null);
        await BestaetigenAsync(AnmeldungTobias);
        await ZustellenAsync();
        Assert.Equal(1L, await WertAsync<long>(
            Verbindung, "SELECT count(*) FROM personen.freizeit_rolle WHERE \"FreizeitId\" = @f AND \"PersonId\" = @p AND \"Rolle\" = 2", ("f", X), ("p", TobiasId)));
        Anonym = Merken(Factory.CreateClient());
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }

        await Factory.DisposeAsync();
    }

    /// <summary>Stellt die Outbox zweimal zu (Ereignis und Folge-Ereignisse).</summary>
    public async Task ZustellenAsync()
    {
        await Factory.OutboxJetztZustellenAsync(Abbruch);
        await Factory.OutboxJetztZustellenAsync(Abbruch);
    }

    /// <summary>Katrin meldet ein Kind über einen Anmeldelink an (die Outbox ist danach zugestellt).</summary>
    public async Task<Guid> KindAnmeldenAsync(Guid kind, string? anmeldelink = null)
    {
        var anmeldung = await AnmeldenAsync(Katrin, anmeldelink ?? TeilnehmendeX, kind);
        await ZustellenAsync();
        return anmeldung;
    }

    /// <summary>
    /// Story 2-7: Eine Gast-Familie meldet ein Kind über den Anmeldelink an (ohne Konto, die Outbox ist danach zugestellt); liefert
    /// Gast-Token, Kind und Anmeldung.
    /// </summary>
    public async Task<(string Token, Guid Kind, Guid Anmeldung)> GastAnmeldenAsync(string mail, string kind, string? anmeldelink = null)
    {
        using var gast = await PostAsync(Anonym, "/api/personen/oeffentlich/gaeste", new Zeltlotse.Api.Contracts.Personen.GastAnlegen("Sabine", "Beispiel", mail, kind, "Beispiel", new DateOnly(2016, 4, 1)));
        Assert.Equal(HttpStatusCode.Created, gast.StatusCode);
        var json = await JsonAsync(gast);
        var (token, kindId) = (json.GetProperty("gastToken").GetString()!, json.GetProperty("kindPersonId").GetGuid());
        using var anmeldung = await PostAsync(Anonym, $"/api/freizeiten/oeffentlich/{anmeldelink ?? TeilnehmendeX}/anmeldungen", new GastAnmeldungAbsenden(token, kindId, true));
        Assert.Equal(HttpStatusCode.Created, anmeldung.StatusCode);
        var anmeldungId = (await JsonAsync(anmeldung)).GetProperty("anmeldungId").GetGuid();
        await ZustellenAsync();
        return (token, kindId, anmeldungId);
    }

    public async Task BestaetigenAsync(Guid anmeldung, Guid? freizeit = null)
    {
        using var antwort = await PostAsync(Miriam, $"/api/freizeiten/{freizeit ?? X}/anmeldungen/{anmeldung}/bestaetigen");
        Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);
    }

    public async Task AbsagenAsync(Guid anmeldung)
    {
        using var antwort = await PostAsync(Miriam, $"/api/freizeiten/{X}/anmeldungen/{anmeldung}/absagen");
        Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);
    }

    /// <summary>Miriam setzt die Fragen für Teilnehmende einer Freizeit (Autosave-PATCH mit dem aktuellen Wert als altem Wert).</summary>
    public async Task FragenAsync(Guid freizeit, string kennungen)
    {
        var auswahl = await JsonAsync(await Miriam.GetAsync($"/api/freizeiten/{freizeit}/fragen", Abbruch));
        var koerper = new Dictionary<string, object?>
        {
            ["version"] = auswahl.GetProperty("version").GetUInt32(),
            ["alterWert"] = auswahl.GetProperty("teilnehmende").GetString(),
            ["teilnehmende"] = kennungen,
        };
        using var antwort = await SendenAsync(Miriam, HttpMethod.Patch, $"/api/freizeiten/{freizeit}/fragen", koerper);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
    }

    /// <summary>Katrin beantwortet eine Frage im Pass eines Kindes (erste Antwort, Version 0).</summary>
    public async Task AntwortAsync(Guid person, string kennung, string wert)
    {
        using var antwort = await SendenAsync(
            Katrin, HttpMethod.Patch, $"/api/ich/freizeitpass/{person}/antworten/{kennung}",
            new Dictionary<string, object?> { ["version"] = 0, ["alterWert"] = null, ["wert"] = wert });
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
    }

    public static string PassPfad(Guid freizeit, Guid person) => $"/api/freizeiten/{freizeit}/personen/freizeitpass/{person}";

    public static string NotizenPfad(Guid freizeit, Guid anmeldung) => $"/api/freizeiten/{freizeit}/anmeldungen/{anmeldung}/notizen";

    public static string PersonenlistePfad(Guid freizeit) => $"/api/freizeiten/{freizeit}/personenliste";

    /// <summary>Story 2-6: Freizeitpass-Stand der Freizeit (Leitung).</summary>
    public static string StandPfad(Guid freizeit) => $"/api/freizeiten/{freizeit}/personen/freizeitpass-stand";

    /// <summary>Story 2-6: Erinnern zum Freizeitpass (Leitung).</summary>
    public static string ErinnernPfad(Guid freizeit) => $"/api/freizeiten/{freizeit}/personen/freizeitpass/erinnerungen";

    /// <summary>Stellt die Uhr auf die nächste volle Sekunde (die Datenbank speichert Mikrosekunden) und liefert diesen Zeitpunkt.</summary>
    public DateTimeOffset VolleSekunde()
    {
        var jetzt = Factory.Uhr.GetUtcNow();
        var ziel = new DateTimeOffset(jetzt.Ticks - (jetzt.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero).AddSeconds(1);
        Factory.Uhr.SetUtcNow(ziel);
        return ziel;
    }

    private static async Task<Guid> AnmeldenAsync(HttpClient client, string anmeldelink, Guid? person)
    {
        using var antwort = await PostAsync(client, "/api/ich/anmeldungen", new AnmeldungAbsenden(anmeldelink, person, true));
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        return (await JsonAsync(antwort)).GetProperty("anmeldungId").GetGuid();
    }

    private static string TokenAus(string adresse) => adresse[(adresse.LastIndexOf('/') + 1)..];

    private async Task<Dictionary<string, string>> AnmeldelinksAsync(Guid freizeit) =>
        (await JsonAsync(await Miriam.GetAsync($"/api/freizeiten/{freizeit}/freizeitseite", Abbruch))).GetProperty("anmeldelinks").EnumerateArray()
        .ToDictionary(l => l.GetProperty("art").GetString()!, l => l.GetProperty("adresse").GetString()!);

    private HttpClient Merken(HttpClient client)
    {
        _clients.Add(client);
        return client;
    }
}

/// <summary>Kleine Leseschritte über JSON für die Tests zu Story 2-5.</summary>
internal static class EinsichtJson
{
    public static IReadOnlyList<string?> Kennungen(JsonElement pass) =>
        [.. pass.GetProperty("fragen").EnumerateArray().Select(f => f.GetProperty("kennung").GetString())];
}
