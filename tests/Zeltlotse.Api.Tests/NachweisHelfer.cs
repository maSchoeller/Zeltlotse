using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.SharedKernel.Dateien;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Aufbau der Nachweis-Tests (FR-8 bis FR-12) über die echten Endpunkte mit S3-Container: Organisation A (Andrea Org-Admin) mit
/// dem Zeltlager (Leitung Jonas, Miriam, Tobias), Namen gesetzt, Outbox zugestellt (Freizeit-Rollen und automatische
/// Einsichten in Personen).
/// </summary>
internal sealed class NachweisSzenario : IAsyncDisposable
{
    public const string Gerd = OrgHelfer.Gerd;
    public const string Andrea = OrgHelfer.Andrea;
    public const string Jonas = OrgHelfer.Jonas;
    public const string Lea = OrgHelfer.Lea;
    public const string Miriam = FreizeitHelfer.Miriam;
    public const string Tobias = FreizeitHelfer.Tobias;

    public static readonly byte[] Pdf = [.. "%PDF-1.7\nFührungszeugnis\n"u8, 1, 2, 3];
    public static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52];

    private readonly List<HttpClient> _clients = [];

    private NachweisSzenario(ZeltlotseApiFactory factory, string verbindung, string bucket)
    {
        Factory = factory;
        Verbindung = verbindung;
        Bucket = bucket;
    }

    public ZeltlotseApiFactory Factory { get; }

    public string Verbindung { get; }

    public string Bucket { get; }

    public Guid OrgId { get; private set; }

    public Guid FreizeitId { get; private set; }

    public HttpClient GerdClient { get; private set; } = null!;

    public HttpClient AndreaClient { get; private set; } = null!;

    public HttpClient JonasClient { get; private set; } = null!;

    public HttpClient MiriamClient { get; private set; } = null!;

    public HttpClient TobiasClient { get; private set; } = null!;

    public string FreizeitPfad => $"/api/freizeiten/{FreizeitId}/personen/nachweise";

    public string OrgPfad => $"/api/organisationen/{OrgId}/personen/nachweise";

    /// <summary>Factory mit Global Admin und S3; noch ohne Organisation. <paramref name="vorbereiten"/> läuft vor dem Start.</summary>
    public static async Task<NachweisSzenario> LeerAsync(PostgresContainer postgres, S3Container s3, Action<ZeltlotseApiFactory>? vorbereiten = null)
    {
        var verbindung = await postgres.NeueMigrierteDatenbankAsync();
        var bucket = S3Container.NeuerBucket();
        var factory = OrgHelfer.Factory(verbindung);
        foreach (var (schluessel, wert) in s3.Einstellungen(bucket))
        {
            factory.Einstellungen[schluessel] = wert;
        }

        vorbereiten?.Invoke(factory);
        var szenario = new NachweisSzenario(factory, verbindung, bucket);
        szenario.GerdClient = await szenario.AngemeldetAsync(Gerd);
        return szenario;
    }

    /// <summary>Organisation A, Zeltlager mit Jonas, Miriam und Tobias als Leitung, Outbox zugestellt.</summary>
    public static async Task<NachweisSzenario> ErstellenAsync(PostgresContainer postgres, S3Container s3, Action<ZeltlotseApiFactory>? vorbereiten = null)
    {
        var szenario = await LeerAsync(postgres, s3, vorbereiten);
        (szenario.OrgId, szenario.AndreaClient) = await OrgHelfer.OrganisationMitOrgAdminAsync(szenario.Factory, szenario.GerdClient, "Jugendwerk Musterstadt", Andrea);
        szenario._clients.Add(szenario.AndreaClient);
        szenario.FreizeitId = await FreizeitHelfer.AnlegenAsync(szenario.AndreaClient, szenario.OrgId);
        szenario.JonasClient = await szenario.LeitungAsync(Jonas, "Jonas", "Hüttner");
        szenario.MiriamClient = await szenario.LeitungAsync(Miriam, "Miriam", "Zeltner");
        szenario.TobiasClient = await szenario.LeitungAsync(Tobias, "Tobias", "Wanderer");
        await FreizeitHelfer.NameSetzenAsync(szenario.AndreaClient, "Andrea", "Beispiel");
        await szenario.Factory.OutboxJetztZustellenAsync(Abbruch);
        return szenario;
    }

    public async Task<HttpClient> AngemeldetAsync(string mail)
    {
        var client = await AngemeldeterClientAsync(Factory, mail);
        _clients.Add(client);
        return client;
    }

    private async Task<HttpClient> LeitungAsync(string mail, string vorname, string nachname)
    {
        var client = await FreizeitHelfer.LeitungAsync(Factory, AndreaClient, OrgId, FreizeitId, mail);
        _clients.Add(client);
        await FreizeitHelfer.NameSetzenAsync(client, vorname, nachname);
        return client;
    }

    public Task<Guid> PersonIdAsync(string mail) => AnmeldeHelfer.PersonIdAsync(Verbindung, mail);

    // ---------- eigene Nachweise ----------

    public static Task<HttpResponseMessage> AnlegenAsync(HttpClient client, string? art, string? datum) =>
        PostAsync(client, "/api/ich/nachweise", new Dictionary<string, string?> { ["art"] = art, ["ausstellungsdatum"] = datum });

    /// <summary>Legt einen Nachweis an, lädt optional eine Datei hoch und liefert die Kennung.</summary>
    public static async Task<Guid> NachweisAsync(HttpClient client, string art, string datum, byte[]? datei = null)
    {
        Guid id;
        using (var antwort = await AnlegenAsync(client, art, datum))
        {
            Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
            id = (await antwort.Content.ReadFromJsonAsync<NachweisHinzugefuegt>(Abbruch))!.NachweisId;
        }

        if (datei is not null)
        {
            using var hochladen = await HochladenAsync(client, id, datei);
            Assert.Equal(HttpStatusCode.OK, hochladen.StatusCode);
        }

        return id;
    }

    public static async Task<HttpResponseMessage> HochladenAsync(HttpClient client, Guid nachweisId, byte[]? inhalt, string dateiname = "nachweis.pdf", string feld = "datei")
    {
        var token = await AntiforgeryTokenAsync(client);
        using var anfrage = new HttpRequestMessage(HttpMethod.Post, $"/api/ich/nachweise/{nachweisId}/datei");
        anfrage.Headers.Add(Kopfzeilen.Antiforgery, token);
        var formular = new MultipartFormDataContent();
        if (inhalt is not null)
        {
            var datei = new ByteArrayContent(inhalt);
            datei.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            formular.Add(datei, feld, dateiname);
        }
        else
        {
            formular.Add(new StringContent("x"), "anderes");
        }

        anfrage.Content = formular;
        return await client.SendAsync(anfrage, Abbruch);
    }

    public static async Task<NachweiseAntwort> EigeneAsync(HttpClient client)
    {
        using var antwort = await client.GetAsync("/api/ich/nachweise", Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return (await antwort.Content.ReadFromJsonAsync<NachweiseAntwort>(Abbruch))!;
    }

    public static async Task<JsonElement> EigeneJsonAsync(HttpClient client)
    {
        using var antwort = await client.GetAsync("/api/ich/nachweise", Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return await JsonAsync(antwort);
    }

    public static Task<HttpResponseMessage> FreigebenAsync(HttpClient client, Guid einsichtId) =>
        PostAsync(client, $"/api/ich/nachweise/einsichten/{einsichtId}/freigeben");

    /// <summary>Die einzige offene Anfrage der Person (automatisch aus der Freizeit-Rolle).</summary>
    public static async Task<Guid> AnfrageAsync(HttpClient client) => Assert.Single((await EigeneAsync(client)).Anfragen).Id;

    // ---------- prüfen ----------

    public static Task<HttpResponseMessage> PruefenAsync(HttpClient client, string basis, Guid einsichtId) =>
        PostAsync(client, $"{basis}/einsichten/{einsichtId}/pruefen");

    public static Task<HttpResponseMessage> BestaetigenAsync(HttpClient client, string basis, Guid einsichtId, Guid nachweisId) =>
        PostAsync(client, $"{basis}/einsichten/{einsichtId}/nachweise/{nachweisId}/bestaetigen");

    public static Task<HttpResponseMessage> AblehnenAsync(HttpClient client, string basis, Guid einsichtId, Guid nachweisId, string? hinweis) =>
        PostAsync(client, $"{basis}/einsichten/{einsichtId}/nachweise/{nachweisId}/ablehnen", new NachweisAblehnen(hinweis));

    public static Task<HttpResponseMessage> DateiAsync(HttpClient client, string basis, Guid einsichtId, Guid nachweisId) =>
        client.GetAsync($"{basis}/einsichten/{einsichtId}/nachweise/{nachweisId}/datei", Abbruch);

    public static Task<HttpResponseMessage> AnfragenAsync(HttpClient client, string basis, Guid? personId) =>
        PostAsync(client, $"{basis}/anfragen", new EinsichtAnfragen(personId));

    public static async Task<EinsichtDetail> DetailAsync(HttpClient client, string basis, Guid einsichtId)
    {
        using var antwort = await client.GetAsync($"{basis}/einsichten/{einsichtId}", Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return (await antwort.Content.ReadFromJsonAsync<EinsichtDetail>(Abbruch))!;
    }

    public static async Task<List<PruefPerson>> ListeAsync(HttpClient client, string basis)
    {
        using var antwort = await client.GetAsync(basis, Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return (await antwort.Content.ReadFromJsonAsync<List<PruefPerson>>(Abbruch))!;
    }

    /// <summary>Jonas legt drei Nachweise an (zwei mit Datei), gibt frei, Miriam übernimmt; liefert Einsicht und Kennungen.</summary>
    public async Task<(Guid Einsicht, Guid Fuehrungszeugnis, Guid ErsteHilfe, Guid Juleica)> FreigegebenUndUebernommenAsync()
    {
        var fz = await NachweisAsync(JonasClient, "fuehrungszeugnis", "2025-11-03", Pdf);
        var eh = await NachweisAsync(JonasClient, "ersteHilfe", "2025-04-15", Png);
        var ju = await NachweisAsync(JonasClient, "juleica", "2024-05-20");
        var einsicht = await AnfrageAsync(JonasClient);
        using (var freigeben = await FreigebenAsync(JonasClient, einsicht))
        {
            Assert.Equal(HttpStatusCode.OK, freigeben.StatusCode);
        }

        using (var pruefen = await PruefenAsync(MiriamClient, FreizeitPfad, einsicht))
        {
            Assert.Equal(HttpStatusCode.NoContent, pruefen.StatusCode);
        }

        return (einsicht, fz, eh, ju);
    }

    /// <summary>Die rohen Bytes eines Objekts im Bucket (nur für Prüfungen).</summary>
    public async Task<byte[]> RohAsync(S3Container s3, Guid nachweisId)
    {
        using var roh = s3.RoherClient();
        using var objekt = await roh.GetObjectAsync(Bucket, $"personen/{nachweisId}", Abbruch);
        using var puffer = new MemoryStream();
        await objekt.ResponseStream.CopyToAsync(puffer, Abbruch);
        return puffer.ToArray();
    }

    public async Task<byte[]?> EntschluesseltAsync(Guid nachweisId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IGeschuetzteDateien>().LesenAsync("personen", nachweisId, Abbruch);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients.Prepend(GerdClient))
        {
            client?.Dispose();
        }

        await Factory.DisposeAsync();
    }
}
