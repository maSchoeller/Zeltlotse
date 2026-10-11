using System.Net;
using System.Net.Http.Json;
using Zeltlotse.Api.Contracts.Personen;

namespace Zeltlotse.Ui.Api;

/// <summary>Ergebnis von <see cref="PersonenApiClient.KindAnlegenAsync"/>: die Kennung des Kindes oder die Fehler an den Feldern.</summary>
public sealed record KindAnlegenErgebnis(Guid? PersonId, string? VornameFehler, string? NachnameFehler, string? GeburtsdatumFehler);

/// <summary>Ergebnis von <see cref="PersonenApiClient.ElternteilEinladenAsync"/>: Kennung, Fehler am Feld oder „schon zwei Eltern“ (<c>409</c>).</summary>
public sealed record ElternteilEinladenErgebnis(Guid? Id, string? MailFehler, bool Voll);

/// <summary>Ergebnis von <see cref="PersonenApiClient.ProfilUebergebenAsync"/>: Kennung, Fehler am Feld oder „noch nicht volljährig“ (<c>409</c>).</summary>
public sealed record UebergabeErgebnis(Guid? Id, string? MailFehler, bool NichtVolljaehrig);

/// <summary>Ergebnis des Annehmens eines Zugangslinks: angenommen, nicht (mehr) gültig (<c>404</c>) oder ein Konflikt (<c>409</c>).</summary>
public enum AnnahmeErgebnis
{
    Angenommen,
    NichtGueltig,
    Konflikt,
}

/// <summary>
/// Typisierter Client des Moduls Personen (AD-29, handgeschrieben): das eigene Profil und das Familienkonto (Kinder, zweiter
/// Elternteil, Profil-Übergabe). Tokens gehen nur im Körper. Unerwartete Antworten enden in einer
/// <see cref="HttpRequestException"/>.
/// </summary>
public sealed class PersonenApiClient(HttpClient http)
{
    /// <summary>Pfad des eigenen Profils, auch für den Autosave-Baustein (<c>PATCH</c>, AD-19).</summary>
    public const string ProfilPfad = "/api/ich/profil";

    /// <summary>Pfad der eigenen Familie (AD-26).</summary>
    public const string FamiliePfad = "/api/ich/familie";

    /// <summary>Pfad eines Kinderprofils, auch für den Autosave-Baustein (<c>PATCH</c>, AD-19).</summary>
    public static string KindPfad(Guid personId) => $"{FamiliePfad}/kinder/{personId}";

    /// <summary><c>GET /api/ich/profil</c>: das Profil oder <c>null</c> (<c>404</c>), solange die Person noch nicht angelegt ist.</summary>
    public async Task<ProfilAntwort?> ProfilLadenAsync(CancellationToken abbruch = default)
    {
        using var antwort = await http.GetAsync(ProfilPfad, abbruch);
        if (antwort.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        antwort.EnsureSuccessStatusCode();
        return await antwort.Content.ReadFromJsonAsync<ProfilAntwort>(ApiJson.Optionen, abbruch)
               ?? throw new HttpRequestException("Leere Antwort beim Laden des Profils.");
    }

    /// <summary><c>POST /api/ich/profil</c>: legt die Person an, falls sie fehlt (idempotent), und liefert das Profil.</summary>
    public async Task<ProfilAntwort> ProfilSicherstellenAsync(CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsync(ProfilPfad, content: null, abbruch);
        antwort.EnsureSuccessStatusCode();
        return await antwort.Content.ReadFromJsonAsync<ProfilAntwort>(ApiJson.Optionen, abbruch)
               ?? throw new HttpRequestException("Leere Antwort beim Anlegen des Profils.");
    }

    /// <summary>Lädt das Profil und legt es an, falls es fehlt.</summary>
    public async Task<ProfilAntwort> ProfilLadenOderAnlegenAsync(CancellationToken abbruch = default) =>
        await ProfilLadenAsync(abbruch) ?? await ProfilSicherstellenAsync(abbruch);

    /// <summary><c>GET /api/ich/familie</c>: die eigene Familie; ohne Familie sind alle Listen leer.</summary>
    public async Task<FamilieAntwort> FamilieLadenAsync(CancellationToken abbruch = default)
    {
        using var antwort = await http.GetAsync(FamiliePfad, abbruch);
        antwort.EnsureSuccessStatusCode();
        return await antwort.Content.ReadFromJsonAsync<FamilieAntwort>(ApiJson.Optionen, abbruch)
               ?? throw new HttpRequestException("Leere Antwort beim Laden der Familie.");
    }

    /// <summary><c>POST /api/ich/familie/kinder</c>: <c>201</c> mit Kennung; <c>400</c> mit den Fehlern an den Feldern.</summary>
    public async Task<KindAnlegenErgebnis> KindAnlegenAsync(string? vorname, string? nachname, DateOnly? geburtsdatum, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync($"{FamiliePfad}/kinder", new KindHinzufuegen(vorname, nachname, geburtsdatum), ApiJson.Optionen, abbruch);
        if (antwort.StatusCode == HttpStatusCode.BadRequest)
        {
            var vornameFehler = await ApiFehler.FeldFehlerAsync(antwort, KindFelder.Vorname, abbruch);
            var nachnameFehler = await ApiFehler.FeldFehlerAsync(antwort, KindFelder.Nachname, abbruch);
            var geburtsdatumFehler = await ApiFehler.FeldFehlerAsync(antwort, KindFelder.Geburtsdatum, abbruch);
            if (vornameFehler is not null || nachnameFehler is not null || geburtsdatumFehler is not null)
            {
                return new KindAnlegenErgebnis(null, vornameFehler, nachnameFehler, geburtsdatumFehler);
            }
        }

        antwort.EnsureSuccessStatusCode();
        var inhalt = await antwort.Content.ReadFromJsonAsync<KindHinzugefuegt>(ApiJson.Optionen, abbruch)
                     ?? throw new HttpRequestException("Leere Antwort beim Anlegen des Kindes.");
        return new KindAnlegenErgebnis(inhalt.PersonId, null, null, null);
    }

    /// <summary><c>POST /api/ich/familie/einladungen</c>: <c>202</c> mit Kennung; <c>400</c> am Feld <c>mail</c>; <c>409</c> bei zwei Eltern.</summary>
    public async Task<ElternteilEinladenErgebnis> ElternteilEinladenAsync(string? mail, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync($"{FamiliePfad}/einladungen", new ElternteilEinladen(mail), ApiJson.Optionen, abbruch);
        if (antwort.StatusCode == HttpStatusCode.Conflict)
        {
            return new ElternteilEinladenErgebnis(null, null, Voll: true);
        }

        if (antwort.StatusCode == HttpStatusCode.BadRequest && await ApiFehler.FeldFehlerAsync(antwort, "mail", abbruch) is { } mailFehler)
        {
            return new ElternteilEinladenErgebnis(null, mailFehler, Voll: false);
        }

        antwort.EnsureSuccessStatusCode();
        var inhalt = await antwort.Content.ReadFromJsonAsync<ElternteilEingeladen>(ApiJson.Optionen, abbruch)
                     ?? throw new HttpRequestException("Leere Antwort beim Einladen.");
        return new ElternteilEinladenErgebnis(inhalt.Id, null, Voll: false);
    }

    /// <summary>
    /// <c>POST /api/ich/familie/kinder/{personId}/uebergabe</c>: <c>202</c> mit Kennung; <c>400</c> am Feld <c>mail</c>; <c>409</c>
    /// noch nicht volljährig. Ohne frische Anmeldung (<c>401</c>) führt der Sitzungs-Handler zur Anmeldung.
    /// </summary>
    public async Task<UebergabeErgebnis> ProfilUebergebenAsync(Guid kindId, string? mail, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync($"{KindPfad(kindId)}/uebergabe", new ProfilUebergeben(mail), ApiJson.Optionen, abbruch);
        if (antwort.StatusCode == HttpStatusCode.Conflict)
        {
            return new UebergabeErgebnis(null, null, NichtVolljaehrig: true);
        }

        if (antwort.StatusCode == HttpStatusCode.BadRequest && await ApiFehler.FeldFehlerAsync(antwort, "mail", abbruch) is { } mailFehler)
        {
            return new UebergabeErgebnis(null, mailFehler, NichtVolljaehrig: false);
        }

        antwort.EnsureSuccessStatusCode();
        var inhalt = await antwort.Content.ReadFromJsonAsync<ProfilUebergabeVerschickt>(ApiJson.Optionen, abbruch)
                     ?? throw new HttpRequestException("Leere Antwort beim Übergeben.");
        return new UebergabeErgebnis(inhalt.Id, null, NichtVolljaehrig: false);
    }

    /// <summary><c>POST /api/personen/familie/einladungen/annehmen</c>: angenommen, nicht gültig (<c>404</c>) oder Konflikt (<c>409</c>).</summary>
    public Task<AnnahmeErgebnis> FamilienEinladungAnnehmenAsync(string token, CancellationToken abbruch = default) =>
        AnnehmenAsync("/api/personen/familie/einladungen/annehmen", new FamilienEinladungAnnehmen(token), abbruch);

    /// <summary><c>POST /api/personen/uebergabe/annehmen</c>: übernommen, nicht gültig (<c>404</c>) oder eigenes Profil (<c>409</c>).</summary>
    public Task<AnnahmeErgebnis> ProfilUebernehmenAsync(string token, CancellationToken abbruch = default) =>
        AnnehmenAsync("/api/personen/uebergabe/annehmen", new ProfilUebernehmen(token), abbruch);

    private async Task<AnnahmeErgebnis> AnnehmenAsync<T>(string pfad, T koerper, CancellationToken abbruch)
    {
        using var antwort = await http.PostAsJsonAsync(pfad, koerper, ApiJson.Optionen, abbruch);
        switch (antwort.StatusCode)
        {
            case HttpStatusCode.NotFound:
                return AnnahmeErgebnis.NichtGueltig;
            case HttpStatusCode.Conflict:
                return AnnahmeErgebnis.Konflikt;
            default:
                antwort.EnsureSuccessStatusCode();
                return AnnahmeErgebnis.Angenommen;
        }
    }
}
