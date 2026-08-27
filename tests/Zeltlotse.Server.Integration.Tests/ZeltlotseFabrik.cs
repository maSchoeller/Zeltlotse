using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Zeltlotse.Core.Konten.Contracts;

namespace Zeltlotse.Server.Integration.Tests;

/// <summary>
/// Startet den echten Server gegen die Testcontainers-Datenbank. Keine
/// Attrappen: Autorisierung, Mandantenfilter und Row-Level-Security laufen so,
/// wie sie auch produktiv laufen.
/// </summary>
public sealed class ZeltlotseFabrik(string verbindung) : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureHostConfiguration(konfiguration => konfiguration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:zeltlotse"] = verbindung,
                ["Zeltlotse:Token:Schluessel"] = "test-schluessel-mit-mindestens-32-zeichen!",
                ["Zeltlotse:Einladung:Basisadresse"] = "https://app.test",
            }));

        return base.CreateHost(builder);
    }

    /// <summary>Ein Client, der wie die Oberfläche auftritt: Token im Kopf, Anfrage-Kennung gesetzt.</summary>
    public HttpClient Angemeldet(string? zugriffstoken = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(KontenKonstanten.AnfrageHeader, "1");

        if (zugriffstoken is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", zugriffstoken);
        }

        return client;
    }

    public async Task<string> EinrichtenAsync(string email, string kennwort)
    {
        var antwort = await Angemeldet().PostAsJsonAsync(
            "/api/einrichtung", new EinrichtungAnfrage("Bea Betreiber", email, kennwort));

        antwort.EnsureSuccessStatusCode();

        return (await antwort.Content.ReadFromJsonAsync<AnmeldungAntwort>())!.Zugriffstoken;
    }

    public async Task<string> AnmeldenAsync(string email, string kennwort)
    {
        var antwort = await Angemeldet().PostAsJsonAsync(
            "/api/auth/anmelden", new AnmeldungAnfrage(email, kennwort));

        antwort.EnsureSuccessStatusCode();

        return (await antwort.Content.ReadFromJsonAsync<AnmeldungAntwort>())!.Zugriffstoken;
    }
}

/// <summary>Kopie der Kopfzeilen-Kennung, damit die Tests nicht in die Scheibe greifen müssen.</summary>
public static class KontenKonstanten
{
    public const string AnfrageHeader = "X-Zeltlotse-Anfrage";
}
