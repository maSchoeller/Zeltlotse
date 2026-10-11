using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Konten;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>Gemeinsame Schritte der Sitzungstests: Factory mit echter Uhrzeit, Aufrufe mit frischem Antiforgery-Token, Anmelden per Link.</summary>
internal static class AnmeldeHelfer
{
    /// <summary>
    /// Factory für Sitzungstests: Die künstliche Uhr startet bei der echten Zeit, damit der Client das dauerhafte
    /// Sitzungs-Cookie (Ablauf = Uhr + 30 Tage) annimmt; die Zugriffsbegrenzung ist hochgesetzt (eigener Test dafür).
    /// </summary>
    public static ZeltlotseApiFactory Factory(string verbindung, bool mitZugriffsbegrenzung = false)
    {
        var factory = new ZeltlotseApiFactory(verbindung);
        factory.Uhr.SetUtcNow(DateTimeOffset.UtcNow);
        if (!mitZugriffsbegrenzung)
        {
            factory.Einstellungen["Zeltlotse:Zugriffsbegrenzung:AnfragenJeMinute"] = "10000";
        }

        return factory;
    }

    /// <summary>Ändernder Aufruf mit frisch geholtem Antiforgery-Token (das Token hängt an der angemeldeten Identität).</summary>
    public static async Task<HttpResponseMessage> SendenAsync(HttpClient client, HttpMethod methode, string pfad, object? koerper = null)
    {
        var token = await AntiforgeryTokenAsync(client);
        using var anfrage = new HttpRequestMessage(methode, pfad);
        anfrage.Headers.Add(Kopfzeilen.Antiforgery, token);
        if (koerper is not null)
        {
            anfrage.Content = koerper is string json
                ? new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                : JsonContent.Create(koerper, koerper.GetType());
        }

        return await client.SendAsync(anfrage, Abbruch);
    }

    public static Task<HttpResponseMessage> PostAsync(HttpClient client, string pfad, object? koerper = null) =>
        SendenAsync(client, HttpMethod.Post, pfad, koerper);

    /// <summary>Fordert Login-Link und Code an und liefert die Kennung der Anfrage.</summary>
    public static async Task<Guid> LinkAnfordernAsync(HttpClient client, string mail)
    {
        using var antwort = await PostAsync(client, "/api/konten/login-link", new LoginLinkAnfordern(mail));
        Assert.Equal(HttpStatusCode.Accepted, antwort.StatusCode);
        return (await antwort.Content.ReadFromJsonAsync<LoginLinkAntwort>(Abbruch))!.AnfrageId;
    }

    /// <summary>Meldet den Client per Login-Link aus der Mail an (neues oder bestehendes Konto).</summary>
    public static async Task AnmeldenAsync(ZeltlotseApiFactory factory, HttpClient client, string mail)
    {
        var vorher = factory.Mails.An(mail).Count;
        await LinkAnfordernAsync(client, mail);
        var token = (await factory.Mails.WartenAufAsync(mail, vorher + 1)).Token;
        using var antwort = await PostAsync(client, "/api/konten/login-link/einloesen", new LoginLinkEinloesen(token));
        Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);
    }

    /// <summary>Ein neuer Client der Factory, angemeldet für die Mail-Adresse.</summary>
    public static async Task<HttpClient> AngemeldeterClientAsync(ZeltlotseApiFactory factory, string mail)
    {
        var client = factory.CreateClient();
        await AnmeldenAsync(factory, client, mail);
        return client;
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage antwort) =>
        await antwort.Content.ReadFromJsonAsync<JsonElement>(Abbruch);

    /// <summary>Die Namen der Eigenschaften eines JSON-Objekts, sortiert.</summary>
    public static IReadOnlyList<string> Namen(JsonElement objekt) =>
        [.. objekt.EnumerateObject().Select(e => e.Name).Order(StringComparer.Ordinal)];

    /// <summary>Ein Wert aus der Datenbank (rohes SQL nur für Prüfungen im Test).</summary>
    public static async Task<T> WertAsync<T>(string verbindung, string sql, params (string Name, object Wert)[] parameter)
    {
        await using var db = new NpgsqlConnection(verbindung);
        await db.OpenAsync(Abbruch);
        await using var befehl = new NpgsqlCommand(sql, db);
        foreach (var (name, wert) in parameter)
        {
            befehl.Parameters.AddWithValue(name, wert);
        }

        return (T)(await befehl.ExecuteScalarAsync(Abbruch))!;
    }

    public static async Task AusfuehrenAsync(string verbindung, string sql, params (string Name, object Wert)[] parameter)
    {
        await using var db = new NpgsqlConnection(verbindung);
        await db.OpenAsync(Abbruch);
        await using var befehl = new NpgsqlCommand(sql, db);
        foreach (var (name, wert) in parameter)
        {
            befehl.Parameters.AddWithValue(name, wert);
        }

        await befehl.ExecuteNonQueryAsync(Abbruch);
    }

    /// <summary>Anzahl der Sicherheitsprotokoll-Zeilen einer Art (optional einer Person).</summary>
    public static Task<long> ProtokollAsync(string verbindung, Zeltlotse.SharedKernel.Sicherheitsprotokoll.SicherheitsEreignisArt art, Guid? personId = null) =>
        personId is { } person
            ? WertAsync<long>(verbindung, "SELECT count(*) FROM betrieb.sicherheitsprotokoll WHERE \"Art\" = @art AND \"PersonId\" = @person", ("art", (short)art), ("person", person))
            : WertAsync<long>(verbindung, "SELECT count(*) FROM betrieb.sicherheitsprotokoll WHERE \"Art\" = @art", ("art", (short)art));

    /// <summary>Die PersonId des Kontos einer Mail-Adresse.</summary>
    public static Task<Guid> PersonIdAsync(string verbindung, string mail) =>
        WertAsync<Guid>(verbindung, "SELECT \"PersonId\" FROM konten.konto WHERE \"Mail\" = @mail", ("mail", mail));
}
