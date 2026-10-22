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

/// <summary>Ergebnis von <see cref="PersonenApiClient.NachweisAnlegenAsync"/>: Kennung, Fehler an den Feldern oder „Art schon vorhanden“ (<c>409</c>).</summary>
public sealed record NachweisAnlegenErgebnis(Guid? NachweisId, string? ArtFehler, string? DatumFehler, bool Vorhanden);

/// <summary>
/// Ergebnis von <see cref="PersonenApiClient.DateiHochladenAsync"/>: Hochladedatum, der Fehler am Feld <c>datei</c>, gerade zur
/// Prüfung freigegeben (<c>409</c>) oder nicht gefunden (<c>404</c>).
/// </summary>
public sealed record DateiHochladenErgebnis(DateTimeOffset? HochgeladenAm, string? DateiFehler, bool InPruefung = false, bool NichtGefunden = false);

/// <summary>
/// Ergebnis von <see cref="PersonenApiClient.FreigebenAsync"/>: freigegeben bis, nicht (mehr) da (<c>404</c>) oder nicht möglich
/// (<c>409</c>: kein passender Nachweis mit Datei, oder die Freigabe läuft schon).
/// </summary>
public sealed record FreigabeErgebnis(DateTimeOffset? FreigegebenBis, bool NichtGefunden, bool OhneDatei);

/// <summary>Ergebnis von <see cref="PersonenApiClient.ErinnernAsync"/>.</summary>
public enum ErinnerungErgebnis
{
    Gesendet,

    /// <summary><c>409</c>: heute schon erinnert.</summary>
    SchonHeute,

    /// <summary><c>404</c>: keine Person des Teams dieser Freizeit.</summary>
    NichtGefunden,
}

/// <summary>Ergebnis von <see cref="PersonenApiClient.KontoLoeschenAsync"/>.</summary>
public enum KontoLoeschenErgebnis
{
    Geloescht,

    /// <summary><c>401</c>: Die Anmeldung ist älter als 10 Minuten; der Sitzungs-Handler führt zur Anmeldung mit Rückweg.</summary>
    AnmeldungNoetig,
}

/// <summary>Ergebnis von <see cref="PersonenApiClient.PruefenAsync"/>.</summary>
public enum PruefErgebnis
{
    Gebunden,
    NichtGefunden,
    NichtFreigegeben,
}

/// <summary>
/// Wo Nachweise geprüft werden: im Freizeit-Kontext (Leitung) oder auf Organisationsebene (Org-Admin); <see cref="Basis"/> ist der
/// Pfad der Prüf-Endpunkte.
/// </summary>
public sealed record PruefOrt(string Basis)
{
    public static PruefOrt Freizeit(Guid freizeitId) => new($"/api/freizeiten/{freizeitId}/personen/nachweise");

    public static PruefOrt Organisation(Guid orgId) => new($"/api/organisationen/{orgId}/personen/nachweise");
}

/// <summary>Ergebnis von <see cref="PersonenApiClient.GastAnlegenAsync"/>: die angelegte Gast-Familie oder die Fehler an den Feldern (<c>400</c>).</summary>
public sealed record GastAnlegenErgebnis(GastAngelegt? Angelegt, IReadOnlyDictionary<string, string> Fehler);

/// <summary>
/// Ergebnis von <see cref="PersonenApiClient.GastKindAnlegenAsync"/>: das angelegte Kind, die Fehler an den Feldern (<c>400</c>) oder
/// der Gast-Link gilt nicht (mehr) (<c>404</c>).
/// </summary>
public sealed record GastKindErgebnis(Guid? KindPersonId, IReadOnlyDictionary<string, string> Fehler, bool NichtGefunden);

/// <summary>
/// Ergebnis von <see cref="PersonenApiClient.GastFreizeitpassSpeichernAsync"/>: gespeichert (<c>204</c>), die Fehler je
/// <c>&lt;kennung&gt;.wert</c> bzw. <c>&lt;kennung&gt;.sonstiges</c> (<c>400</c>) oder der Gast-Link gilt nicht (mehr) (<c>404</c>).
/// </summary>
public sealed record GastFreizeitpassErgebnis(bool Gespeichert, IReadOnlyDictionary<string, string> Fehler, bool NichtGefunden);

/// <summary>Ergebnis des Annehmens eines Zugangslinks: angenommen, nicht (mehr) gültig (<c>404</c>) oder ein Konflikt (<c>409</c>).</summary>
public enum AnnahmeErgebnis
{
    Angenommen,
    NichtGueltig,
    Konflikt,
}

/// <summary>
/// Typisierter Client des Moduls Personen (AD-29, handgeschrieben): das eigene Profil, das Familienkonto (Kinder, zweiter
/// Elternteil, Profil-Übergabe), die eigenen Nachweise und das Prüfen von Nachweisen (Leitung, Org-Admin). Tokens gehen nur im Körper. Unerwartete Antworten enden in einer
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

    /// <summary>Pfad des Zugriffsprotokolls (AD-8, AD-26).</summary>
    public const string ZugriffsprotokollPfad = "/api/ich/zugriffsprotokoll";

    /// <summary><c>GET /api/ich/zugriffsprotokoll</c>: wer wann Einsicht hatte, für die Person und ihre Kinder, neueste zuerst.</summary>
    public async Task<List<ZugriffsprotokollEintrag>> ZugriffsprotokollLadenAsync(CancellationToken abbruch = default)
    {
        using var antwort = await http.GetAsync(ZugriffsprotokollPfad, abbruch);
        antwort.EnsureSuccessStatusCode();
        return await antwort.Content.ReadFromJsonAsync<List<ZugriffsprotokollEintrag>>(ApiJson.Optionen, abbruch)
               ?? throw new HttpRequestException("Leere Antwort beim Laden des Zugriffsprotokolls.");
    }

    /// <summary>
    /// <c>POST /api/ich/profil/loeschen</c> (FR-14): löscht Konto und eigene Daten endgültig. Ohne frische Anmeldung (<c>401</c>) führt
    /// der Sitzungs-Handler zur Anmeldung; andere Fehler enden in einer <see cref="HttpRequestException"/>.
    /// </summary>
    public async Task<KontoLoeschenErgebnis> KontoLoeschenAsync(CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsync($"{ProfilPfad}/loeschen", content: null, abbruch);
        if (antwort.StatusCode == HttpStatusCode.Unauthorized)
        {
            return KontoLoeschenErgebnis.AnmeldungNoetig;
        }

        antwort.EnsureSuccessStatusCode();
        return KontoLoeschenErgebnis.Geloescht;
    }

    /// <summary><c>POST /api/personen/familie/einladungen/annehmen</c>: angenommen, nicht gültig (<c>404</c>) oder Konflikt (<c>409</c>).</summary>
    public Task<AnnahmeErgebnis> FamilienEinladungAnnehmenAsync(string token, CancellationToken abbruch = default) =>
        AnnehmenAsync("/api/personen/familie/einladungen/annehmen", new FamilienEinladungAnnehmen(token), abbruch);

    /// <summary><c>POST /api/personen/uebergabe/annehmen</c>: übernommen, nicht gültig (<c>404</c>) oder eigenes Profil (<c>409</c>).</summary>
    public Task<AnnahmeErgebnis> ProfilUebernehmenAsync(string token, CancellationToken abbruch = default) =>
        AnnehmenAsync("/api/personen/uebergabe/annehmen", new ProfilUebernehmen(token), abbruch);

    /// <summary>
    /// <c>POST /api/personen/oeffentlich/gaeste</c> (anonym, Gast-Anmeldung): legt Elternteil, Familie, Kind und Gast-Link an; <c>400</c>
    /// mit den Fehlern je Feld.
    /// </summary>
    public async Task<GastAnlegenErgebnis> GastAnlegenAsync(GastAnlegen anfrage, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync("/api/personen/oeffentlich/gaeste", anfrage, ApiJson.Optionen, abbruch);
        if (antwort.StatusCode == HttpStatusCode.BadRequest && await ApiFehler.AlleFeldFehlerAsync(antwort, abbruch) is { Count: > 0 } fehler)
        {
            return new GastAnlegenErgebnis(null, fehler);
        }

        antwort.EnsureSuccessStatusCode();
        return new GastAnlegenErgebnis(
            await antwort.Content.ReadFromJsonAsync<GastAngelegt>(ApiJson.Optionen, abbruch) ?? throw new HttpRequestException("Leere Antwort beim Anlegen des Gasts."),
            new Dictionary<string, string>());
    }

    /// <summary><c>POST /api/personen/oeffentlich/gaeste/{gastToken}/kinder</c> (anonym): ein weiteres Kind der Gast-Familie.</summary>
    public async Task<GastKindErgebnis> GastKindAnlegenAsync(string gastToken, GastKindHinzufuegen anfrage, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync($"/api/personen/oeffentlich/gaeste/{Uri.EscapeDataString(gastToken)}/kinder", anfrage, ApiJson.Optionen, abbruch);
        if (antwort.StatusCode == HttpStatusCode.NotFound)
        {
            return new GastKindErgebnis(null, new Dictionary<string, string>(), NichtGefunden: true);
        }

        if (antwort.StatusCode == HttpStatusCode.BadRequest && await ApiFehler.AlleFeldFehlerAsync(antwort, abbruch) is { Count: > 0 } fehler)
        {
            return new GastKindErgebnis(null, fehler, NichtGefunden: false);
        }

        antwort.EnsureSuccessStatusCode();
        var inhalt = await antwort.Content.ReadFromJsonAsync<GastKindAngelegt>(ApiJson.Optionen, abbruch)
                     ?? throw new HttpRequestException("Leere Antwort beim Anlegen des Kindes.");
        return new GastKindErgebnis(inhalt.KindPersonId, new Dictionary<string, string>(), NichtGefunden: false);
    }

    /// <summary>
    /// <c>POST /api/ich/gastprofil/uebernehmen</c>: übernommen, nicht gültig (<c>404</c>) oder das Konto hat schon eigene Angaben
    /// (<c>409</c>). Das Token geht nur im Körper.
    /// </summary>
    public Task<AnnahmeErgebnis> GastprofilUebernehmenAsync(string gastToken, CancellationToken abbruch = default) =>
        AnnehmenAsync("/api/ich/gastprofil/uebernehmen", new GastprofilUebernehmen(gastToken), abbruch);

    /// <summary>Pfad der eigenen Nachweise (AD-26).</summary>
    public const string NachweisePfad = "/api/ich/nachweise";

    /// <summary>Pfad eines eigenen Nachweises, auch für den Autosave-Baustein (<c>PATCH</c>, AD-19).</summary>
    public static string NachweisPfad(Guid nachweisId) => $"{NachweisePfad}/{nachweisId}";

    /// <summary>Pfad der Datei eines Nachweises einer Einsicht; der Link öffnet sie im neuen Tab (nur bis zur Entscheidung).</summary>
    public static string EinsichtDateiPfad(PruefOrt ort, Guid einsichtId, Guid nachweisId) =>
        $"{ort.Basis}/einsichten/{einsichtId}/nachweise/{nachweisId}/datei";

    /// <summary><c>GET /api/ich/nachweise</c>: die eigenen Nachweise und offenen Einsichten.</summary>
    public async Task<NachweiseAntwort> NachweiseLadenAsync(CancellationToken abbruch = default)
    {
        using var antwort = await http.GetAsync(NachweisePfad, abbruch);
        antwort.EnsureSuccessStatusCode();
        return await antwort.Content.ReadFromJsonAsync<NachweiseAntwort>(ApiJson.Optionen, abbruch)
               ?? throw new HttpRequestException("Leere Antwort beim Laden der Nachweise.");
    }

    /// <summary><c>POST /api/ich/nachweise</c>: <c>201</c> mit Kennung; <c>400</c> an <c>art</c>/<c>ausstellungsdatum</c>; <c>409</c> Art vorhanden.</summary>
    public async Task<NachweisAnlegenErgebnis> NachweisAnlegenAsync(NachweisArt? art, DateOnly? ausstellungsdatum, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync(NachweisePfad, new NachweisHinzufuegen(art, ausstellungsdatum), ApiJson.Optionen, abbruch);
        if (antwort.StatusCode == HttpStatusCode.Conflict)
        {
            return new NachweisAnlegenErgebnis(null, null, null, Vorhanden: true);
        }

        if (antwort.StatusCode == HttpStatusCode.BadRequest)
        {
            var artFehler = await ApiFehler.FeldFehlerAsync(antwort, NachweisFelder.Art, abbruch);
            var datumFehler = await ApiFehler.FeldFehlerAsync(antwort, NachweisFelder.Ausstellungsdatum, abbruch);
            if (artFehler is not null || datumFehler is not null)
            {
                return new NachweisAnlegenErgebnis(null, artFehler, datumFehler, Vorhanden: false);
            }
        }

        antwort.EnsureSuccessStatusCode();
        var inhalt = await antwort.Content.ReadFromJsonAsync<NachweisHinzugefuegt>(ApiJson.Optionen, abbruch)
                     ?? throw new HttpRequestException("Leere Antwort beim Anlegen des Nachweises.");
        return new NachweisAnlegenErgebnis(inhalt.NachweisId, null, null, Vorhanden: false);
    }

    /// <summary>
    /// <c>POST /api/ich/nachweise/{nachweisId}/datei</c> (Multipart-Feld <c>datei</c>): das Hochladedatum, der Fehler am Feld,
    /// <c>409</c> (gerade zur Prüfung freigegeben) oder <c>404</c>. Netzfehler und andere Antworten enden in einer
    /// <see cref="HttpRequestException"/>; der Eintrag bleibt dann ohne Datei.
    /// </summary>
    public async Task<DateiHochladenErgebnis> DateiHochladenAsync(Guid nachweisId, Stream inhalt, string dateiname, string? contentType, CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(inhalt);
        using var datei = new StreamContent(inhalt);
        datei.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        using var formular = new MultipartFormDataContent { { datei, NachweisFelder.Datei, string.IsNullOrWhiteSpace(dateiname) ? "datei" : dateiname } };
        using var antwort = await http.PostAsync($"{NachweisPfad(nachweisId)}/datei", formular, abbruch);
        if (antwort.StatusCode == HttpStatusCode.BadRequest && await ApiFehler.FeldFehlerAsync(antwort, NachweisFelder.Datei, abbruch) is { } fehler)
        {
            return new DateiHochladenErgebnis(null, fehler);
        }

        switch (antwort.StatusCode)
        {
            case HttpStatusCode.Conflict:
                return new DateiHochladenErgebnis(null, null, InPruefung: true);
            case HttpStatusCode.NotFound:
                return new DateiHochladenErgebnis(null, null, NichtGefunden: true);
        }

        antwort.EnsureSuccessStatusCode();
        var ergebnis = await antwort.Content.ReadFromJsonAsync<NachweisDateiHochgeladen>(ApiJson.Optionen, abbruch)
                       ?? throw new HttpRequestException("Leere Antwort beim Hochladen.");
        return new DateiHochladenErgebnis(ergebnis.HochgeladenAm, null);
    }

    /// <summary><c>POST /api/ich/nachweise/einsichten/{einsichtId}/freigeben</c>.</summary>
    public async Task<FreigabeErgebnis> FreigebenAsync(Guid einsichtId, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsync($"{NachweisePfad}/einsichten/{einsichtId}/freigeben", content: null, abbruch);
        switch (antwort.StatusCode)
        {
            case HttpStatusCode.NotFound:
                return new FreigabeErgebnis(null, NichtGefunden: true, OhneDatei: false);
            case HttpStatusCode.Conflict:
                return new FreigabeErgebnis(null, NichtGefunden: false, OhneDatei: true);
        }

        antwort.EnsureSuccessStatusCode();
        var inhalt = await antwort.Content.ReadFromJsonAsync<EinsichtFreigegeben>(ApiJson.Optionen, abbruch)
                     ?? throw new HttpRequestException("Leere Antwort beim Freigeben.");
        return new FreigabeErgebnis(inhalt.FreigegebenBis, NichtGefunden: false, OhneDatei: false);
    }

    /// <summary><c>GET …/personen/nachweise</c>: die Prüfliste oder <c>null</c> bei <c>403</c>/<c>404</c>.</summary>
    public Task<List<PruefPerson>?> PruefListeLadenAsync(PruefOrt ort, CancellationToken abbruch = default) =>
        LadenOderNullAsync<List<PruefPerson>>(ort.Basis, abbruch);

    /// <summary>
    /// <c>POST /api/freizeiten/{freizeitId}/personen/nachweise/erinnerungen {personId}</c> (Leitung, FR-11): verschickt die
    /// Erinnerungs-Mail über die Outbox und fragt die Einsicht an; <c>409</c> heute schon erinnert, <c>404</c> nicht im Team.
    /// </summary>
    public async Task<ErinnerungErgebnis> ErinnernAsync(Guid freizeitId, Guid personId, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync(
            $"{PruefOrt.Freizeit(freizeitId).Basis}/erinnerungen", new NachweisErinnerungSenden(personId), ApiJson.Optionen, abbruch);
        switch (antwort.StatusCode)
        {
            case HttpStatusCode.Conflict:
                return ErinnerungErgebnis.SchonHeute;
            case HttpStatusCode.NotFound:
                return ErinnerungErgebnis.NichtGefunden;
        }

        antwort.EnsureSuccessStatusCode();
        return ErinnerungErgebnis.Gesendet;
    }

    /// <summary>
    /// <c>GET /api/freizeiten/{freizeitId}/personen/freizeitpass-stand</c> (nur Leitung, Story 2-6): wer offene Angaben im Freizeitpass hat;
    /// <c>null</c> bei <c>403</c>/<c>404</c>.
    /// </summary>
    public Task<FreizeitpassStand?> FreizeitpassStandLadenAsync(Guid freizeitId, CancellationToken abbruch = default) =>
        LadenOderNullAsync<FreizeitpassStand>($"/api/freizeiten/{freizeitId}/personen/freizeitpass-stand", abbruch);

    /// <summary>
    /// <c>POST /api/freizeiten/{freizeitId}/personen/freizeitpass/erinnerungen</c> (nur Leitung, Story 2-6): gesendet, heute schon erinnert
    /// (<c>409</c>) oder nicht gefunden (<c>404</c>: keine aktive Freigabe in dieser Freizeit).
    /// </summary>
    public async Task<ErinnerungErgebnis> FreizeitpassErinnernAsync(Guid freizeitId, Guid personId, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync(
            $"/api/freizeiten/{freizeitId}/personen/freizeitpass/erinnerungen", new FreizeitpassErinnerungSenden(personId), ApiJson.Optionen, abbruch);
        switch (antwort.StatusCode)
        {
            case HttpStatusCode.Conflict:
                return ErinnerungErgebnis.SchonHeute;
            case HttpStatusCode.NotFound:
                return ErinnerungErgebnis.NichtGefunden;
        }

        antwort.EnsureSuccessStatusCode();
        return ErinnerungErgebnis.Gesendet;
    }

    /// <summary><c>GET …/einsichten/{einsichtId}</c>: die Einsicht oder <c>null</c> bei <c>403</c>/<c>404</c>.</summary>
    public Task<EinsichtDetail?> EinsichtLadenAsync(PruefOrt ort, Guid einsichtId, CancellationToken abbruch = default) =>
        LadenOderNullAsync<EinsichtDetail>($"{ort.Basis}/einsichten/{einsichtId}", abbruch);

    /// <summary><c>POST …/anfragen</c>: die Einsicht (neu oder schon offen) oder <c>null</c> bei <c>404</c>.</summary>
    public async Task<Guid?> EinsichtAnfragenAsync(PruefOrt ort, Guid personId, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync($"{ort.Basis}/anfragen", new EinsichtAnfragen(personId), ApiJson.Optionen, abbruch);
        if (antwort.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        antwort.EnsureSuccessStatusCode();
        return (await antwort.Content.ReadFromJsonAsync<EinsichtAngefragt>(ApiJson.Optionen, abbruch)
                ?? throw new HttpRequestException("Leere Antwort beim Anfragen.")).EinsichtId;
    }

    /// <summary><c>POST …/einsichten/{einsichtId}/pruefen</c>: gebunden, nicht übernehmbar (<c>404</c>) oder nicht freigegeben (<c>409</c>).</summary>
    public async Task<PruefErgebnis> PruefenAsync(PruefOrt ort, Guid einsichtId, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsync($"{ort.Basis}/einsichten/{einsichtId}/pruefen", content: null, abbruch);
        switch (antwort.StatusCode)
        {
            case HttpStatusCode.NotFound:
                return PruefErgebnis.NichtGefunden;
            case HttpStatusCode.Conflict:
                return PruefErgebnis.NichtFreigegeben;
        }

        antwort.EnsureSuccessStatusCode();
        return PruefErgebnis.Gebunden;
    }

    /// <summary><c>POST …/nachweise/{nachweisId}/bestaetigen</c>: <c>true</c>, oder <c>false</c> bei <c>404</c> (nicht mehr möglich).</summary>
    public async Task<bool> BestaetigenAsync(PruefOrt ort, Guid einsichtId, Guid nachweisId, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsync($"{ort.Basis}/einsichten/{einsichtId}/nachweise/{nachweisId}/bestaetigen", content: null, abbruch);
        if (antwort.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        antwort.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>
    /// <c>POST …/nachweise/{nachweisId}/ablehnen</c>: <c>(true, null)</c>; Fehler am Feld <c>hinweis</c>; <c>(false, null)</c> bei
    /// <c>404</c> (nicht mehr möglich).
    /// </summary>
    public async Task<(bool Abgelehnt, string? HinweisFehler)> AblehnenAsync(PruefOrt ort, Guid einsichtId, Guid nachweisId, string? hinweis, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync(
            $"{ort.Basis}/einsichten/{einsichtId}/nachweise/{nachweisId}/ablehnen", new NachweisAblehnen(hinweis), ApiJson.Optionen, abbruch);
        if (antwort.StatusCode == HttpStatusCode.NotFound)
        {
            return (false, null);
        }

        if (antwort.StatusCode == HttpStatusCode.BadRequest && await ApiFehler.FeldFehlerAsync(antwort, NachweisFelder.Hinweis, abbruch) is { } fehler)
        {
            return (false, fehler);
        }

        antwort.EnsureSuccessStatusCode();
        return (true, null);
    }

    private async Task<T?> LadenOderNullAsync<T>(string pfad, CancellationToken abbruch)
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

    /// <summary>Pfad des Freizeitpasses einer Person (AD-26, Story 2-4).</summary>
    public static string FreizeitpassPfad(Guid personId) => $"/api/ich/freizeitpass/{personId}";

    /// <summary>Pfad einer Antwort im Freizeitpass, auch für den Autosave-Baustein (<c>PATCH</c>, AD-19).</summary>
    public static string FreizeitpassAntwortPfad(Guid personId, string kennung) => $"{FreizeitpassPfad(personId)}/antworten/{Uri.EscapeDataString(kennung)}";

    /// <summary>
    /// <c>GET /api/ich/freizeitpass/{personId}</c>: der Pass der Person selbst oder eines Kindes der Familie, je Katalogfrage mit Antwort;
    /// <c>null</c> bei <c>404</c> (fremde Person).
    /// </summary>
    public Task<FreizeitpassAnsicht?> FreizeitpassLadenAsync(Guid personId, CancellationToken abbruch = default) =>
        LadenOderNullAsync<FreizeitpassAnsicht>(FreizeitpassPfad(personId), abbruch);

    /// <summary><c>POST …/antworten/{kennung}/bestaetigen</c> („Stimmt“, FR-23): <c>true</c> bei <c>204</c>, <c>false</c> bei <c>404</c>.</summary>
    public async Task<bool> FreizeitpassBestaetigenAsync(Guid personId, string kennung, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsync($"{FreizeitpassAntwortPfad(personId, kennung)}/bestaetigen", content: null, abbruch);
        if (antwort.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        antwort.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>
    /// <c>GET /api/freizeiten/{freizeitId}/personen/freizeitpass/{personId}</c> (Leitung und Team, Story 2-5): Zustand und, wenn sichtbar,
    /// die Fragen der Freigabe mit Antworten; <c>null</c> bei <c>403</c>/<c>404</c>.
    /// </summary>
    public Task<FreizeitpassEinsicht?> FreizeitpassEinsichtLadenAsync(Guid freizeitId, Guid personId, CancellationToken abbruch = default) =>
        LadenOderNullAsync<FreizeitpassEinsicht>($"/api/freizeiten/{freizeitId}/personen/freizeitpass/{personId}", abbruch);

    /// <summary>
    /// <c>GET /api/freizeiten/{freizeitId}/personen/notfallordner?person=…</c> (Leitung und Team, Story 2-11): die Pässe der genannten
    /// Personen, soweit bestätigt und im Freigabefenster, sortiert nach Nachname (höchstens 150 Kennungen je Abruf); <c>null</c> bei <c>403</c>/<c>404</c>.
    /// </summary>
    public Task<List<NotfallordnerPerson>?> NotfallordnerLadenAsync(Guid freizeitId, IEnumerable<Guid> personIds, CancellationToken abbruch = default) =>
        LadenOderNullAsync<List<NotfallordnerPerson>>(
            $"/api/freizeiten/{freizeitId}/personen/notfallordner?{string.Join("&", personIds.Select(p => $"{NotfallordnerAbfrage.Parameter}={p}"))}",
            abbruch);

    /// <summary>
    /// Story 6-5: <c>GET /api/freizeiten/{freizeitId}/personen/notfallkarte</c> (Leitung und Team, FR-65): die Notfallangaben aller bestätigten
    /// Personen der Freizeit für den Offline-Speicher, nur die nötigen Fragen; <c>null</c> bei <c>403</c>/<c>404</c>.
    /// </summary>
    public Task<List<NotfallkartePerson>?> NotfallkarteLadenAsync(Guid freizeitId, CancellationToken abbruch = default) =>
        LadenOderNullAsync<List<NotfallkartePerson>>($"/api/freizeiten/{freizeitId}/personen/notfallkarte", abbruch);

    /// <summary>Pfad des Freizeitpasses eines Kindes über den Gast-Link (anonym, Story 2-4).</summary>
    public static string GastFreizeitpassPfad(string gastToken, Guid personId) =>
        $"/api/personen/oeffentlich/gaeste/{Uri.EscapeDataString(gastToken)}/freizeitpass/{personId}";

    /// <summary><c>GET</c> des Freizeitpasses über den Gast-Link; <c>null</c>, wenn der Gast-Link nicht (mehr) gilt oder das Kind fremd ist.</summary>
    public Task<FreizeitpassAnsicht?> GastFreizeitpassLadenAsync(string gastToken, Guid personId, CancellationToken abbruch = default) =>
        LadenOderNullAsync<FreizeitpassAnsicht>(GastFreizeitpassPfad(gastToken, personId), abbruch);

    /// <summary><c>POST</c> des Formulars über den Gast-Link: gespeichert, Fehler je Feld oder nicht (mehr) gültig.</summary>
    public async Task<GastFreizeitpassErgebnis> GastFreizeitpassSpeichernAsync(
        string gastToken, Guid personId, FreizeitpassFormular formular, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync(GastFreizeitpassPfad(gastToken, personId), formular, ApiJson.Optionen, abbruch);
        if (antwort.StatusCode == HttpStatusCode.NotFound)
        {
            return new GastFreizeitpassErgebnis(false, new Dictionary<string, string>(), NichtGefunden: true);
        }

        if (antwort.StatusCode == HttpStatusCode.BadRequest && await ApiFehler.AlleFeldFehlerAsync(antwort, abbruch) is { Count: > 0 } fehler)
        {
            return new GastFreizeitpassErgebnis(false, fehler, NichtGefunden: false);
        }

        antwort.EnsureSuccessStatusCode();
        return new GastFreizeitpassErgebnis(true, new Dictionary<string, string>(), NichtGefunden: false);
    }

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
