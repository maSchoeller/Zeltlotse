using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Amazon.S3;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>Story 2-7: Hochladen als Multipart, Pfade und Prüfungen der Dateien von Dokumenten und Org-Dokumenten.</summary>
internal static class DokumentHelfer
{
    public static readonly byte[] Pdf = [.. "%PDF-1.7\nInfobrief Zeltlager\n"u8, 1, 2, 3];
    public static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52];

    public static string FreizeitPfad(Guid freizeit) => $"/api/freizeiten/{freizeit}/dokumente";

    public static string OrgPfad(Guid organisation) => $"/api/organisationen/{organisation}/dokumente";

    /// <summary>Lädt per Multipart hoch: Feld <c>datei</c> (ohne Inhalt fehlt es) und optional <c>sichtbarkeit</c>.</summary>
    public static async Task<HttpResponseMessage> HochladenAsync(
        HttpClient client, string pfad, byte[]? inhalt, string dateiname = "infobrief.pdf", string? sichtbarkeit = null)
    {
        var token = await AntiforgeryTokenAsync(client);
        using var anfrage = new HttpRequestMessage(HttpMethod.Post, pfad);
        anfrage.Headers.Add(Kopfzeilen.Antiforgery, token);
        var formular = new MultipartFormDataContent();
        if (inhalt is not null)
        {
            var datei = new ByteArrayContent(inhalt);
            datei.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            formular.Add(datei, "datei", dateiname);
        }
        else
        {
            formular.Add(new StringContent("x"), "anderes");
        }

        if (sichtbarkeit is not null)
        {
            formular.Add(new StringContent(sichtbarkeit), "sichtbarkeit");
        }

        anfrage.Content = formular;
        return await client.SendAsync(anfrage, Abbruch);
    }

    /// <summary>Lädt hoch, erwartet <c>201</c> und liefert die Kennung.</summary>
    public static async Task<Guid> HochgeladenAsync(HttpClient client, string pfad, byte[] inhalt, string dateiname, string? sichtbarkeit = null)
    {
        using var antwort = await HochladenAsync(client, pfad, inhalt, dateiname, sichtbarkeit);
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        return (await JsonAsync(antwort)).GetProperty("id").GetGuid();
    }

    /// <summary>Die Titel einer Liste <c>{dokumente: [...]}</c> in Reihenfolge.</summary>
    public static async Task<IReadOnlyList<string?>> TitelAsync(HttpClient client, string pfad)
    {
        using var antwort = await client.GetAsync(pfad, Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return [.. (await JsonAsync(antwort)).GetProperty("dokumente").EnumerateArray().Select(d => d.GetProperty("titel").GetString())];
    }

    /// <summary>Die Einträge einer Liste <c>{dokumente: [...]}</c>.</summary>
    public static async Task<IReadOnlyList<JsonElement>> DokumenteAsync(HttpClient client, string pfad)
    {
        using var antwort = await client.GetAsync(pfad, Abbruch);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return [.. (await JsonAsync(antwort)).GetProperty("dokumente").EnumerateArray().Select(d => d.Clone())];
    }

    /// <summary>Prüft eine Datei-Antwort: Inhalt, Content-Type, immer als Anhang mit Dateinamen und <c>nosniff</c>.</summary>
    public static async Task DateiPruefenAsync(HttpResponseMessage antwort, byte[] inhalt, string contentType, string dateiname)
    {
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        Assert.Equal(inhalt, await antwort.Content.ReadAsByteArrayAsync(Abbruch));
        Assert.Equal(contentType, antwort.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", antwort.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal(dateiname, antwort.Content.Headers.ContentDisposition?.FileNameStar ?? antwort.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Contains("nosniff", antwort.Headers.GetValues("X-Content-Type-Options"));
    }

    /// <summary>Die rohen Bytes eines Objekts im Bucket oder <c>null</c>, wenn es fehlt.</summary>
    public static async Task<byte[]?> RohAsync(S3Container s3, string bucket, string schluessel)
    {
        using var roh = s3.RoherClient();
        try
        {
            using var objekt = await roh.GetObjectAsync(bucket, schluessel, Abbruch);
            using var puffer = new MemoryStream();
            await objekt.ResponseStream.CopyToAsync(puffer, Abbruch);
            return puffer.ToArray();
        }
        catch (AmazonS3Exception fehler) when (fehler.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}
