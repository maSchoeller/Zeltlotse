using System.Net;
using System.Net.Http.Json;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Api.Contracts.Gemeinsam;

namespace Zeltlotse.Ui.Api;

/// <summary>Ergebnis von <see cref="FreizeitenApiClient.AnlegenAsync"/>: die Kennung oder die Fehler an den Feldern.</summary>
public sealed record FreizeitAnlegenErgebnis(Guid? Id, string? NameFehler, string? ErsterTagFehler, string? LetzterTagFehler);

/// <summary>
/// Ergebnis von <see cref="FreizeitenApiClient.AnmeldelinkErneuernAsync"/>: der neue Link, nicht (mehr) verfügbar (<c>403</c>/<c>404</c>)
/// oder die Freizeit ist abgeschlossen (<c>409</c>).
/// </summary>
public sealed record AnmeldelinkErneuernErgebnis(Anmeldelink? Link, bool Abgeschlossen);

/// <summary>
/// Ergebnis der Anmelde-Aufrufe (<see cref="FreizeitenApiClient.GastAnmeldenAsync"/>, <see cref="FreizeitenApiClient.AnmeldenAsync"/>):
/// die Kennung der Anmeldung, die Fehler an den Feldern (<c>400</c>), nicht (mehr) gültig (<c>404</c>) oder schon angemeldet (<c>409</c>).
/// </summary>
public sealed record AnmeldungErgebnis(Guid? AnmeldungId, IReadOnlyDictionary<string, string> Fehler, bool NichtGefunden, bool SchonAngemeldet);

/// <summary>
/// Ergebnis von <see cref="FreizeitenApiClient.BestaetigenAsync"/> und <see cref="FreizeitenApiClient.AbsagenAsync"/>: entschieden
/// (<c>204</c>), schon entschieden (<c>409</c>) oder nicht (mehr) verfügbar (<c>403</c>/<c>404</c>).
/// </summary>
public enum EntscheidungErgebnis
{
    Entschieden,
    SchonEntschieden,
    NichtVerfuegbar,
}

/// <summary>
/// Ergebnis von <see cref="FreizeitenApiClient.TeamNotizAnlegenAsync"/> (Story 2-5): die Kennung der Notiz, der Fehler am Feld <c>text</c>
/// oder nicht (mehr) verfügbar (<c>403</c>/<c>404</c>).
/// </summary>
public sealed record TeamNotizErgebnis(Guid? Id, string? TextFehler, bool NichtVerfuegbar);

/// <summary>Ergebnis von <see cref="FreizeitenApiClient.LeitungEinladenAsync"/>: die Kennung der Einladung oder der Fehler an der Mail-Adresse.</summary>
public sealed record LeitungEinladenErgebnis(Guid? Id, string? MailFehler);

/// <summary>
/// Typisierter Client des Moduls Freizeiten (AD-29, handgeschrieben): Freizeit-Kontext, Freizeiten einer Organisation
/// (Liste, Anlegen, Verwalten, Leitung einladen), Leitung übernehmen, Startseite der Freizeit, Warnhinweise zu Nachweisen, die
/// eigenen Freizeiten, die Einstellungen der Freizeitseite mit den Anmeldelinks und die beiden öffentlichen Seiten eines
/// Anmeldelinks (Freizeitseite, Vertrauensseite).
/// <c>403</c> und <c>404</c> beim Laden heißen „nicht verfügbar“ (<c>null</c>); andere Fehler enden in einer
/// <see cref="HttpRequestException"/>.
/// </summary>
public sealed class FreizeitenApiClient(HttpClient http)
{
    /// <summary><c>GET /api/freizeiten/{freizeitId}/kontext</c>: der Kontext oder <c>null</c> („nicht verfügbar“).</summary>
    public Task<FreizeitKontext?> KontextLadenAsync(Guid freizeitId, CancellationToken abbruch = default) =>
        LadenAsync<FreizeitKontext>($"/api/freizeiten/{freizeitId}/kontext", abbruch);

    /// <summary><c>GET /api/organisationen/{orgId}/freizeiten</c>: Freizeiten der Organisation mit erlaubten Aktionen oder <c>null</c>.</summary>
    public Task<FreizeitenListe?> ListeLadenAsync(Guid orgId, CancellationToken abbruch = default) =>
        LadenAsync<FreizeitenListe>($"/api/organisationen/{orgId}/freizeiten", abbruch);

    /// <summary><c>POST /api/organisationen/{orgId}/freizeiten</c>: <c>201</c> mit Kennung; <c>400</c> mit den Fehlern an den Feldern.</summary>
    public async Task<FreizeitAnlegenErgebnis> AnlegenAsync(Guid orgId, string? name, DateOnly? ersterTag, DateOnly? letzterTag, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync($"/api/organisationen/{orgId}/freizeiten", new FreizeitAnlegen(name, ersterTag, letzterTag), ApiJson.Optionen, abbruch);
        if (antwort.StatusCode == HttpStatusCode.BadRequest)
        {
            var nameFehler = await ApiFehler.FeldFehlerAsync(antwort, "name", abbruch);
            var ersterFehler = await ApiFehler.FeldFehlerAsync(antwort, "ersterTag", abbruch);
            var letzterFehler = await ApiFehler.FeldFehlerAsync(antwort, "letzterTag", abbruch);
            if (nameFehler is not null || ersterFehler is not null || letzterFehler is not null)
            {
                return new FreizeitAnlegenErgebnis(null, nameFehler, ersterFehler, letzterFehler);
            }
        }

        antwort.EnsureSuccessStatusCode();
        var inhalt = await antwort.Content.ReadFromJsonAsync<FreizeitAngelegt>(ApiJson.Optionen, abbruch)
                     ?? throw new HttpRequestException("Leere Antwort beim Anlegen der Freizeit.");
        return new FreizeitAnlegenErgebnis(inhalt.Id, null, null, null);
    }

    /// <summary><c>GET /api/organisationen/{orgId}/freizeiten/{freizeitId}</c>: Verwaltung der Freizeit oder <c>null</c>.</summary>
    public Task<FreizeitVerwaltung?> VerwaltungLadenAsync(Guid orgId, Guid freizeitId, CancellationToken abbruch = default) =>
        LadenAsync<FreizeitVerwaltung>($"/api/organisationen/{orgId}/freizeiten/{freizeitId}", abbruch);

    /// <summary><c>POST …/freizeiten/{freizeitId}/leitung/einladungen</c>: <c>202</c> mit Kennung; <c>400</c> mit dem Fehler an <c>mail</c>.</summary>
    public async Task<LeitungEinladenErgebnis> LeitungEinladenAsync(Guid orgId, Guid freizeitId, string? mail, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync(
            $"/api/organisationen/{orgId}/freizeiten/{freizeitId}/leitung/einladungen", new LeitungEinladen(mail), ApiJson.Optionen, abbruch);
        if (antwort.StatusCode == HttpStatusCode.BadRequest && await ApiFehler.FeldFehlerAsync(antwort, "mail", abbruch) is { } mailFehler)
        {
            return new LeitungEinladenErgebnis(null, mailFehler);
        }

        antwort.EnsureSuccessStatusCode();
        var inhalt = await antwort.Content.ReadFromJsonAsync<LeitungEingeladen>(ApiJson.Optionen, abbruch)
                     ?? throw new HttpRequestException("Leere Antwort beim Einladen der Leitung.");
        return new LeitungEinladenErgebnis(inhalt.Id, null);
    }

    /// <summary>
    /// <c>POST /api/freizeiten/leitung/annehmen</c>: die Freizeit bei <c>200</c>, <c>null</c> bei <c>404</c> (nicht mehr gültig
    /// oder für eine andere Mail-Adresse). Das Token geht nur im Körper.
    /// </summary>
    public async Task<Guid?> LeitungAnnehmenAsync(string token, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync("/api/freizeiten/leitung/annehmen", new LeitungAnnehmen(token), ApiJson.Optionen, abbruch);
        if (antwort.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        antwort.EnsureSuccessStatusCode();
        var inhalt = await antwort.Content.ReadFromJsonAsync<LeitungAngenommen>(ApiJson.Optionen, abbruch)
                     ?? throw new HttpRequestException("Leere Antwort beim Annehmen der Leitung.");
        return inhalt.FreizeitId;
    }

    /// <summary><c>GET /api/freizeiten/{freizeitId}</c>: Startseite der Freizeit oder <c>null</c>.</summary>
    public Task<FreizeitAntwort?> LadenAsync(Guid freizeitId, CancellationToken abbruch = default) =>
        LadenAsync<FreizeitAntwort>($"/api/freizeiten/{freizeitId}", abbruch);

    /// <summary>
    /// <c>GET /api/freizeiten/{freizeitId}/nachweis-warnungen</c> (Leitung, FR-11): Personen des Teams mit Warnhinweisen zu
    /// Pflicht-Nachweisen oder <c>null</c> bei <c>403</c>/<c>404</c>.
    /// </summary>
    public Task<NachweisWarnungen?> NachweisWarnungenLadenAsync(Guid freizeitId, CancellationToken abbruch = default) =>
        LadenAsync<NachweisWarnungen>($"/api/freizeiten/{freizeitId}/nachweis-warnungen", abbruch);

    /// <summary><c>GET /api/ich/freizeiten</c>: die eigenen Freizeiten mit den eigenen Rollen.</summary>
    public async Task<List<MeineFreizeit>> MeineLadenAsync(CancellationToken abbruch = default)
    {
        using var antwort = await http.GetAsync("/api/ich/freizeiten", abbruch);
        antwort.EnsureSuccessStatusCode();
        return await antwort.Content.ReadFromJsonAsync<List<MeineFreizeit>>(ApiJson.Optionen, abbruch)
               ?? throw new HttpRequestException("Leere Antwort beim Laden der eigenen Freizeiten.");
    }

    /// <summary>Pfad der Einstellungen der Freizeitseite, auch für den Autosave-Baustein (<c>PATCH</c>, AD-19).</summary>
    public static string FreizeitseitePfad(Guid freizeitId) => $"/api/freizeiten/{freizeitId}/freizeitseite";

    /// <summary>Pfad der Fragen-Auswahl des Freizeitpasses, auch für den Autosave-Baustein (<c>PATCH</c>, AD-19, Story 2-4).</summary>
    public static string FragenPfad(Guid freizeitId) => $"/api/freizeiten/{freizeitId}/fragen";

    /// <summary>
    /// <c>GET /api/freizeiten/{freizeitId}/fragen</c> (Leitung, FR-22): die gewählten Fragen je Zielgruppe mit Version und der Katalog
    /// oder <c>null</c> bei <c>403</c>/<c>404</c>.
    /// </summary>
    public Task<FragenAuswahl?> FragenLadenAsync(Guid freizeitId, CancellationToken abbruch = default) =>
        LadenAsync<FragenAuswahl>(FragenPfad(freizeitId), abbruch);

    /// <summary>
    /// <c>GET /api/freizeiten/{freizeitId}/freizeitseite</c> (Leitung): Beschreibung, Ort, Höchstzahl und die Anmeldelinks oder
    /// <c>null</c> bei <c>403</c>/<c>404</c>.
    /// </summary>
    public Task<FreizeitseiteEinstellungen?> FreizeitseiteLadenAsync(Guid freizeitId, CancellationToken abbruch = default) =>
        LadenAsync<FreizeitseiteEinstellungen>(FreizeitseitePfad(freizeitId), abbruch);

    /// <summary>
    /// <c>POST /api/freizeiten/{freizeitId}/anmeldelinks/{art}/erneuern</c> (Leitung): der neue Link; der bisherige ist danach
    /// ungültig.
    /// </summary>
    public async Task<AnmeldelinkErneuernErgebnis> AnmeldelinkErneuernAsync(Guid freizeitId, AnmeldelinkArt art, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsync($"/api/freizeiten/{freizeitId}/anmeldelinks/{AnmeldelinkArten.Routenwert(art)}/erneuern", content: null, abbruch);
        if (antwort.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return new AnmeldelinkErneuernErgebnis(null, Abgeschlossen: false);
        }

        if (antwort.StatusCode == HttpStatusCode.Conflict)
        {
            return new AnmeldelinkErneuernErgebnis(null, Abgeschlossen: true);
        }

        antwort.EnsureSuccessStatusCode();
        var link = await antwort.Content.ReadFromJsonAsync<Anmeldelink>(ApiJson.Optionen, abbruch)
                   ?? throw new HttpRequestException("Leere Antwort beim Erneuern des Anmeldelinks.");
        return new AnmeldelinkErneuernErgebnis(link, Abgeschlossen: false);
    }

    /// <summary>
    /// <c>GET /api/freizeiten/oeffentlich/{token}</c> (anonym): die öffentliche Freizeitseite oder <c>null</c>, wenn der Link nicht
    /// (mehr) gültig ist.
    /// </summary>
    public Task<Freizeitseite?> FreizeitseiteOeffentlichLadenAsync(string token, CancellationToken abbruch = default) =>
        LadenAsync<Freizeitseite>($"/api/freizeiten/oeffentlich/{Uri.EscapeDataString(token)}", abbruch);

    /// <summary>
    /// <c>GET /api/freizeiten/oeffentlich/{token}/vertrauensseite</c> (anonym): die Angaben der Vertrauensseite oder <c>null</c>, wenn
    /// der Link nicht (mehr) gültig ist.
    /// </summary>
    public Task<Vertrauensseite?> VertrauensseiteLadenAsync(string token, CancellationToken abbruch = default) =>
        LadenAsync<Vertrauensseite>($"/api/freizeiten/oeffentlich/{Uri.EscapeDataString(token)}/vertrauensseite", abbruch);

    /// <summary>
    /// <c>POST /api/freizeiten/oeffentlich/{token}/anmeldungen</c> (anonym, Gast-Anmeldung): Anmeldelink im Pfad, Gast-Token und Kind
    /// im Körper.
    /// </summary>
    public async Task<AnmeldungErgebnis> GastAnmeldenAsync(
        string anmeldelinkToken, string gastToken, Guid kindPersonId, bool teilnahmebedingungenBestaetigt, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync(
            $"/api/freizeiten/oeffentlich/{Uri.EscapeDataString(anmeldelinkToken)}/anmeldungen",
            new GastAnmeldungAbsenden(gastToken, kindPersonId, teilnahmebedingungenBestaetigt),
            ApiJson.Optionen,
            abbruch);
        return await AnmeldungErgebnisAsync(antwort, abbruch);
    }

    /// <summary>
    /// <c>POST /api/ich/anmeldungen</c> (mit Konto): die angemeldete Person selbst (<paramref name="personId"/> leer) oder ein Kind unter
    /// 18 der eigenen Familie. Das Token geht nur im Körper.
    /// </summary>
    public async Task<AnmeldungErgebnis> AnmeldenAsync(string anmeldelinkToken, Guid? personId, bool teilnahmebedingungenBestaetigt, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync(
            "/api/ich/anmeldungen", new AnmeldungAbsenden(anmeldelinkToken, personId, teilnahmebedingungenBestaetigt), ApiJson.Optionen, abbruch);
        return await AnmeldungErgebnisAsync(antwort, abbruch);
    }

    /// <summary><c>GET /api/ich/anmeldungen</c>: die eigenen Anmeldungen und die der Kinder der Familie, über Organisationen hinweg.</summary>
    public async Task<List<AnmeldungZeile>> MeineAnmeldungenLadenAsync(CancellationToken abbruch = default)
    {
        using var antwort = await http.GetAsync("/api/ich/anmeldungen", abbruch);
        antwort.EnsureSuccessStatusCode();
        return await antwort.Content.ReadFromJsonAsync<List<AnmeldungZeile>>(ApiJson.Optionen, abbruch)
               ?? throw new HttpRequestException("Leere Antwort beim Laden der Anmeldungen.");
    }

    /// <summary><c>GET /api/freizeiten/gast/{gastToken}</c> (anonym): die Anmeldungen der Gast-Familie oder <c>null</c>, wenn der Gast-Link nicht (mehr) gilt.</summary>
    public Task<GastLinkAnsicht?> GastLinkLadenAsync(string gastToken, CancellationToken abbruch = default) =>
        LadenAsync<GastLinkAnsicht>($"/api/freizeiten/gast/{Uri.EscapeDataString(gastToken)}", abbruch);

    /// <summary>
    /// <c>GET /api/freizeiten/{freizeitId}/anmeldungen</c> (Leitung, Story 2-3): Höchstzahl, bestätigte Teilnehmende und alle
    /// Anmeldungen oder <c>null</c> bei <c>403</c>/<c>404</c>.
    /// </summary>
    public Task<AnmeldungenUebersicht?> AnmeldungenAsync(Guid freizeitId, CancellationToken abbruch = default) =>
        LadenAsync<AnmeldungenUebersicht>($"/api/freizeiten/{freizeitId}/anmeldungen", abbruch);

    /// <summary><c>POST /api/freizeiten/{freizeitId}/anmeldungen/{anmeldungId}/bestaetigen</c> (Leitung, FR-20).</summary>
    public Task<EntscheidungErgebnis> BestaetigenAsync(Guid freizeitId, Guid anmeldungId, CancellationToken abbruch = default) =>
        EntscheidenAsync($"/api/freizeiten/{freizeitId}/anmeldungen/{anmeldungId}/bestaetigen", abbruch);

    /// <summary><c>POST /api/freizeiten/{freizeitId}/anmeldungen/{anmeldungId}/absagen</c> (Leitung, FR-21).</summary>
    public Task<EntscheidungErgebnis> AbsagenAsync(Guid freizeitId, Guid anmeldungId, CancellationToken abbruch = default) =>
        EntscheidenAsync($"/api/freizeiten/{freizeitId}/anmeldungen/{anmeldungId}/absagen", abbruch);

    /// <summary>
    /// <c>GET /api/freizeiten/{freizeitId}/personenliste</c> (Leitung und Team, Story 2-5): eingegangene und bestätigte Anmeldungen mit
    /// Namen oder <c>null</c> bei <c>403</c>/<c>404</c>.
    /// </summary>
    public Task<List<PersonenlisteZeile>?> PersonenlisteLadenAsync(Guid freizeitId, CancellationToken abbruch = default) =>
        LadenAsync<List<PersonenlisteZeile>>($"/api/freizeiten/{freizeitId}/personenliste", abbruch);

    /// <summary>Pfad der Team-Notizen einer Anmeldung (Story 2-5).</summary>
    public static string TeamNotizenPfad(Guid freizeitId, Guid anmeldungId) => $"/api/freizeiten/{freizeitId}/anmeldungen/{anmeldungId}/notizen";

    /// <summary><c>GET …/anmeldungen/{anmeldungId}/notizen</c> (Story 2-5): neueste zuerst oder <c>null</c> bei <c>403</c>/<c>404</c>.</summary>
    public Task<List<TeamNotiz>?> TeamNotizenLadenAsync(Guid freizeitId, Guid anmeldungId, CancellationToken abbruch = default) =>
        LadenAsync<List<TeamNotiz>>(TeamNotizenPfad(freizeitId, anmeldungId), abbruch);

    /// <summary>
    /// <c>POST …/anmeldungen/{anmeldungId}/notizen</c> (Story 2-5): die Kennung bei <c>201</c>, der Fehler am Feld <c>text</c> bei <c>400</c>
    /// oder „nicht verfügbar“ bei <c>403</c>/<c>404</c>.
    /// </summary>
    public async Task<TeamNotizErgebnis> TeamNotizAnlegenAsync(Guid freizeitId, Guid anmeldungId, string? text, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync(TeamNotizenPfad(freizeitId, anmeldungId), new TeamNotizAnlegen(text), ApiJson.Optionen, abbruch);
        if (antwort.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return new TeamNotizErgebnis(null, null, NichtVerfuegbar: true);
        }

        if (antwort.StatusCode == HttpStatusCode.BadRequest && await ApiFehler.FeldFehlerAsync(antwort, TeamNotizFelder.Text, abbruch) is { } fehler)
        {
            return new TeamNotizErgebnis(null, fehler, NichtVerfuegbar: false);
        }

        antwort.EnsureSuccessStatusCode();
        var inhalt = await antwort.Content.ReadFromJsonAsync<TeamNotizAngelegt>(ApiJson.Optionen, abbruch)
                     ?? throw new HttpRequestException("Leere Antwort beim Anlegen der Team-Notiz.");
        return new TeamNotizErgebnis(inhalt.Id, null, NichtVerfuegbar: false);
    }

    /// <summary><c>DELETE …/notizen/{notizId}</c> (Story 2-5): <c>true</c> bei <c>204</c>, <c>false</c> bei <c>403</c>/<c>404</c>.</summary>
    public async Task<bool> TeamNotizLoeschenAsync(Guid freizeitId, Guid anmeldungId, Guid notizId, CancellationToken abbruch = default)
    {
        using var antwort = await http.DeleteAsync($"{TeamNotizenPfad(freizeitId, anmeldungId)}/{notizId}", abbruch);
        if (antwort.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return false;
        }

        antwort.EnsureSuccessStatusCode();
        return true;
    }

    private async Task<EntscheidungErgebnis> EntscheidenAsync(string pfad, CancellationToken abbruch)
    {
        using var antwort = await http.PostAsync(pfad, content: null, abbruch);
        if (antwort.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return EntscheidungErgebnis.NichtVerfuegbar;
        }

        if (antwort.StatusCode == HttpStatusCode.Conflict)
        {
            return EntscheidungErgebnis.SchonEntschieden;
        }

        antwort.EnsureSuccessStatusCode();
        return EntscheidungErgebnis.Entschieden;
    }

    private static async Task<AnmeldungErgebnis> AnmeldungErgebnisAsync(HttpResponseMessage antwort, CancellationToken abbruch)
    {
        if (antwort.StatusCode == HttpStatusCode.BadRequest && await ApiFehler.AlleFeldFehlerAsync(antwort, abbruch) is { Count: > 0 } fehler)
        {
            return new AnmeldungErgebnis(null, fehler, NichtGefunden: false, SchonAngemeldet: false);
        }

        if (antwort.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict)
        {
            return new AnmeldungErgebnis(
                null, new Dictionary<string, string>(), NichtGefunden: antwort.StatusCode == HttpStatusCode.NotFound, SchonAngemeldet: antwort.StatusCode == HttpStatusCode.Conflict);
        }

        antwort.EnsureSuccessStatusCode();
        var inhalt = await antwort.Content.ReadFromJsonAsync<AnmeldungAngelegt>(ApiJson.Optionen, abbruch)
                     ?? throw new HttpRequestException("Leere Antwort beim Anmelden.");
        return new AnmeldungErgebnis(inhalt.AnmeldungId, new Dictionary<string, string>(), NichtGefunden: false, SchonAngemeldet: false);
    }

    private async Task<T?> LadenAsync<T>(string pfad, CancellationToken abbruch)
        where T : class
    {
        using var antwort = await http.GetAsync(pfad, abbruch);
        if (antwort.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return null;
        }

        antwort.EnsureSuccessStatusCode();
        return await antwort.Content.ReadFromJsonAsync<T>(ApiJson.Optionen, abbruch)
               ?? throw new HttpRequestException($"Leere Antwort von {pfad}.");
    }
}
