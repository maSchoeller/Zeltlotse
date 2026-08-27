using System.Net;
using System.Net.Http.Json;
using Zeltlotse.Core.Freizeiten.Contracts;
using Zeltlotse.Core.Konten.Contracts;
using Zeltlotse.Core.Organisationen.Contracts;

namespace Zeltlotse.Client.Dienste;

/// <summary>
/// Warum ein Zugriff nicht ging. Fehlende Zugehörigkeit und fehlende Rolle
/// sind verschiedene Dinge — wer die falsche Begründung liest, sucht den Fehler
/// an der falschen Stelle.
/// </summary>
public enum Zugriffsproblem
{
    Keins = 0,
    FremdeOrganisation = 1,
    FehlendeRolle = 2,
}

/// <summary>Ergebnis eines Aufrufs: Wert oder Fehlertext, nie beides.</summary>
public sealed record Ergebnis<T>(T? Wert, string? Fehler, Zugriffsproblem Problem = Zugriffsproblem.Keins)
{
    public bool Gelungen => Fehler is null;

    public bool Gesperrt => Problem != Zugriffsproblem.Keins;

    public static Ergebnis<T> Gut(T? wert) => new(wert, null);

    public static Ergebnis<T> Schlecht(string fehler, Zugriffsproblem problem = Zugriffsproblem.Keins)
        => new(default, fehler, problem);
}

/// <summary>
/// Der einzige Weg zur Schnittstelle. Läuft ein Aufruf in eine 401-Antwort,
/// wird genau einmal erneuert und der Aufruf wiederholt — danach nicht mehr,
/// sonst dreht sich das im Kreis.
/// </summary>
public sealed class ZeltlotseApi(HttpClient http, Sitzung sitzung)
{
    public Task<Ergebnis<List<FreizeitDto>>> MeineFreizeitenAsync()
        => HolenAsync<List<FreizeitDto>>("/api/freizeiten/meine");

    public Task<Ergebnis<OrganisationDto>> OrganisationAsync(string slug)
        => HolenAsync<OrganisationDto>($"/api/o/{slug}");

    public Task<Ergebnis<List<FreizeitDto>>> FreizeitenAsync(string slug)
        => HolenAsync<List<FreizeitDto>>($"/api/o/{slug}/freizeiten");

    public Task<Ergebnis<FreizeitDto>> FreizeitAsync(string slug, Guid id)
        => HolenAsync<FreizeitDto>($"/api/o/{slug}/freizeiten/{id}");

    public Task<Ergebnis<FreizeitDto>> FreizeitAnlegenAsync(string slug, FreizeitAnlegen anfrage)
        => SendenAsync<FreizeitDto>(HttpMethod.Post, $"/api/o/{slug}/freizeiten", anfrage);

    public Task<Ergebnis<bool>> FreizeitAendernAsync(string slug, Guid id, FreizeitAendern anfrage)
        => OhneInhaltAsync(HttpMethod.Put, $"/api/o/{slug}/freizeiten/{id}", anfrage);

    public Task<Ergebnis<bool>> FreizeitLoeschenAsync(string slug, Guid id)
        => OhneInhaltAsync(HttpMethod.Delete, $"/api/o/{slug}/freizeiten/{id}", null);

    public Task<Ergebnis<List<FreizeitTeamDto>>> TeamAsync(string slug, Guid id)
        => HolenAsync<List<FreizeitTeamDto>>($"/api/o/{slug}/freizeiten/{id}/team");

    public Task<Ergebnis<bool>> TeamHinzufuegenAsync(string slug, Guid id, TeamZuordnung person)
        => OhneInhaltAsync(HttpMethod.Post, $"/api/o/{slug}/freizeiten/{id}/team", person);

    public Task<Ergebnis<List<KandidatDto>>> KandidatenAsync(string slug, Guid id)
        => HolenAsync<List<KandidatDto>>($"/api/o/{slug}/freizeiten/{id}/kandidaten");

    public Task<Ergebnis<EinladungErzeugtDto>> EinladungErneuernAsync(string slug, Guid id)
        => SendenAsync<EinladungErzeugtDto>(
            HttpMethod.Post, $"/api/o/{slug}/einladungen/{id}/erneuern", null);

    public Task<Ergebnis<bool>> TeamEntfernenAsync(string slug, Guid id, Guid nutzerId)
        => OhneInhaltAsync(HttpMethod.Delete, $"/api/o/{slug}/freizeiten/{id}/team/{nutzerId}", null);

    public Task<Ergebnis<EinladungErzeugtDto>> TeamEinladenAsync(
        string slug, Guid id, EinladungAnlegen anfrage)
        => SendenAsync<EinladungErzeugtDto>(
            HttpMethod.Post, $"/api/o/{slug}/freizeiten/{id}/einladungen", anfrage);

    public Task<Ergebnis<List<MitgliedDto>>> MitgliederAsync(string slug)
        => HolenAsync<List<MitgliedDto>>($"/api/o/{slug}/mitglieder");

    public Task<Ergebnis<List<OffeneEinladungDto>>> OffeneEinladungenAsync(string slug)
        => HolenAsync<List<OffeneEinladungDto>>($"/api/o/{slug}/einladungen");

    public Task<Ergebnis<EinladungErzeugtDto>> EinladenAsync(string slug, EinladungAnlegen anfrage)
        => SendenAsync<EinladungErzeugtDto>(HttpMethod.Post, $"/api/o/{slug}/einladungen", anfrage);

    public Task<Ergebnis<bool>> LoeschantragAsync(string slug)
        => OhneInhaltAsync(HttpMethod.Post, $"/api/o/{slug}/loeschantrag", null);

    public Task<Ergebnis<bool>> LoeschantragZuruecknehmenAsync(string slug)
        => OhneInhaltAsync(HttpMethod.Delete, $"/api/o/{slug}/loeschantrag", null);

    public Task<Ergebnis<List<PapierkorbEintragDto>>> PapierkorbAsync(string slug)
        => HolenAsync<List<PapierkorbEintragDto>>($"/api/o/{slug}/papierkorb");

    public Task<Ergebnis<bool>> WiederherstellenAsync(string slug, Guid id)
        => OhneInhaltAsync(HttpMethod.Post, $"/api/o/{slug}/papierkorb/{id}/wiederherstellen", null);

    public Task<Ergebnis<List<OrganisationVerwaltungDto>>> OrganisationenAsync()
        => HolenAsync<List<OrganisationVerwaltungDto>>("/api/verwaltung/organisationen");

    public Task<Ergebnis<NamensvorschlagDto>> SlugVorschauAsync(string name)
        => HolenAsync<NamensvorschlagDto>(
            $"/api/verwaltung/organisationen/slugvorschau?name={Uri.EscapeDataString(name)}");

    public Task<Ergebnis<OrganisationVerwaltungDto>> OrganisationAnlegenAsync(string name)
        => SendenAsync<OrganisationVerwaltungDto>(
            HttpMethod.Post, "/api/verwaltung/organisationen", new OrganisationAnlegen(name));

    public Task<Ergebnis<EinladungErzeugtDto>> LeitungEinladenAsync(Guid id, string name, string email)
        => SendenAsync<EinladungErzeugtDto>(
            HttpMethod.Post,
            $"/api/verwaltung/organisationen/{id}/leitung",
            new EinladungAnlegen(name, email, Einladungsziel.Organisation,
                OrgRolle.OrgAdmin, null, null));

    public Task<Ergebnis<bool>> LoeschungAusfuehrenAsync(Guid id)
        => OhneInhaltAsync(
            HttpMethod.Post, $"/api/verwaltung/organisationen/{id}/loeschung-ausfuehren", null);

    public Task<Ergebnis<List<KontoDto>>> KontenAsync()
        => HolenAsync<List<KontoDto>>("/api/verwaltung/konten");

    public Task<Ergebnis<bool>> SperreAsync(Guid id, bool gesperrt)
        => OhneInhaltAsync(
            HttpMethod.Post, $"/api/verwaltung/konten/{id}/sperre?gesperrt={gesperrt}", null);

    public Task<Ergebnis<bool>> KennwortAendernAsync(string alt, string neu)
        => OhneInhaltAsync(HttpMethod.Post, "/api/konto/kennwort", new KennwortAendern(alt, neu));

    public Task<Ergebnis<EinladungVorschauDto>> EinladungVorschauAsync(string token)
        => HolenAsync<EinladungVorschauDto>($"/api/einladungen/{token}");

    public async Task<bool> EinrichtungNoetigAsync()
    {
        var ergebnis = await HolenAsync<bool>("/api/einrichtung/noetig");

        return ergebnis.Gelungen && ergebnis.Wert;
    }

    // ---------- Innenleben ----------

    private Task<Ergebnis<T>> HolenAsync<T>(string pfad)
        => SendenAsync<T>(HttpMethod.Get, pfad, null);

    private async Task<Ergebnis<T>> SendenAsync<T>(HttpMethod verfahren, string pfad, object? rumpf)
    {
        var antwort = await MitErneuerungAsync(verfahren, pfad, rumpf);

        if (!antwort.IsSuccessStatusCode)
        {
            return Ergebnis<T>.Schlecht(await FehlertextAsync(antwort), Problem(antwort));
        }

        if (antwort.StatusCode == HttpStatusCode.NoContent)
        {
            return Ergebnis<T>.Gut(default);
        }

        return Ergebnis<T>.Gut(await antwort.Content.ReadFromJsonAsync<T>());
    }

    private async Task<Ergebnis<bool>> OhneInhaltAsync(
        HttpMethod verfahren, string pfad, object? rumpf)
    {
        var antwort = await MitErneuerungAsync(verfahren, pfad, rumpf);

        return antwort.IsSuccessStatusCode
            ? Ergebnis<bool>.Gut(true)
            : Ergebnis<bool>.Schlecht(await FehlertextAsync(antwort), Problem(antwort));
    }

    private async Task<HttpResponseMessage> MitErneuerungAsync(
        HttpMethod verfahren, string pfad, object? rumpf)
    {
        HttpResponseMessage antwort;

        try
        {
            antwort = await http.SendAsync(sitzung.Vorbereiten(verfahren, pfad, rumpf));
        }
        catch (HttpRequestException)
        {
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }

        if (antwort.StatusCode != HttpStatusCode.Unauthorized)
        {
            return antwort;
        }

        // Genau ein Versuch. Klappt die Erneuerung nicht, ist die Sitzung
        // wirklich zu Ende.
        if (!await sitzung.ErneuernAsync())
        {
            return antwort;
        }

        return await http.SendAsync(sitzung.Vorbereiten(verfahren, pfad, rumpf));
    }

    private static Zugriffsproblem Problem(HttpResponseMessage antwort) => antwort.StatusCode switch
    {
        HttpStatusCode.NotFound => Zugriffsproblem.FremdeOrganisation,
        HttpStatusCode.Forbidden => Zugriffsproblem.FehlendeRolle,
        _ => Zugriffsproblem.Keins,
    };

    private static async Task<string> FehlertextAsync(HttpResponseMessage antwort)
    {
        if (antwort.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            return "Der Server ist gerade nicht erreichbar.";
        }

        if (antwort.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            var text = await LiesFehlerAsync(antwort);

            return text ?? "Diese Seite gehört zu einer Organisation, zu der du nicht gehörst.";
        }

        return await LiesFehlerAsync(antwort) ?? "Das hat nicht geklappt. Versuche es noch einmal.";
    }

    private static async Task<string?> LiesFehlerAsync(HttpResponseMessage antwort)
    {
        try
        {
            var inhalt = await antwort.Content.ReadFromJsonAsync<Fehlerantwort>();

            return string.IsNullOrWhiteSpace(inhalt?.Fehler) ? null : inhalt.Fehler;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private sealed record Fehlerantwort(string? Fehler);
}
