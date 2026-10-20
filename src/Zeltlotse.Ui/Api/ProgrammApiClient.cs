using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Programm;

namespace Zeltlotse.Ui.Api;

/// <summary>Story 3-1: Ergebnis einer schreibenden Aktion im Programm.</summary>
public enum ProgrammAktionErgebnis
{
    Erfolg,

    /// <summary><c>409 konflikt</c>: z. B. eine andere Person hat den Punkt übernommen, oder eine Mahlzeit lässt sich nicht löschen.</summary>
    Konflikt,

    /// <summary><c>409 freizeit-abgeschlossen</c>: Die Freizeit ist nur noch lesbar.</summary>
    Abgeschlossen,

    /// <summary>Jede andere Antwort, z. B. ein Feldfehler oder nicht (mehr) verfügbar.</summary>
    Fehler,
}

/// <summary>Story 3-1: Ergebnis von Anlegen, Übernehmen und Abgeben; bei Erfolg der Programmpunkt, beim Anlegen sonst die Fehler je Feld.</summary>
public sealed record ProgrammAktion(ProgrammAktionErgebnis Ergebnis, Programmpunkt? Punkt, IReadOnlyDictionary<string, string> Feldfehler)
{
    public static ProgrammAktion Ohne(ProgrammAktionErgebnis ergebnis) => new(ergebnis, null, new Dictionary<string, string>());
}

/// <summary>Story 3-2: Ergebnis von Anlegen, Status setzen und Mitbringen eines Materials; bei Erfolg das Material, beim Anlegen sonst die Fehler je Feld.</summary>
public sealed record MaterialAktion(ProgrammAktionErgebnis Ergebnis, Material? Material, IReadOnlyDictionary<string, string> Feldfehler)
{
    public static MaterialAktion Ohne(ProgrammAktionErgebnis ergebnis) => new(ergebnis, null, new Dictionary<string, string>());
}

/// <summary>Story 3-5: Ergebnis von Anlegen, Übergängen und „Als Vorlage speichern“ einer Aufgabe; beim Anlegen sonst die Fehler je Feld.</summary>
public sealed record AufgabeAktion(ProgrammAktionErgebnis Ergebnis, Aufgabe? Aufgabe, IReadOnlyDictionary<string, string> Feldfehler)
{
    public static AufgabeAktion Ohne(ProgrammAktionErgebnis ergebnis) => new(ergebnis, null, new Dictionary<string, string>());
}

/// <summary>Story 3-5: Ergebnis einer Phasenänderung; bei Erfolg die neue Version.</summary>
public sealed record AufgabePhaseAenderung(ProgrammAktionErgebnis Ergebnis, uint Version);

/// <summary>Story 3-5: Ergebnis von „Vorlage hinzufügen“; bei Erfolg die Vorlage, sonst die Fehler je Feld.</summary>
public sealed record VorlageAktion(ProgrammAktionErgebnis Ergebnis, AufgabenVorlage? Vorlage, IReadOnlyDictionary<string, string> Feldfehler);

/// <summary>Story 3-5: Statuswechsel einer Aufgabe.</summary>
public enum AufgabenUebergang
{
    /// <summary>„Ich übernehme“ (nur für sich selbst).</summary>
    Uebernehmen,

    /// <summary>„Abgeben“ (nur die übernehmende Person).</summary>
    Abgeben,

    /// <summary>„Erledigt“ (jede Person der Freizeit).</summary>
    Erledigt,

    /// <summary>„Wieder offen“ (jede Person der Freizeit).</summary>
    Offen,
}

/// <summary>
/// Typisierter Client des Moduls Programm (AD-29, handgeschrieben). Story 3-1: der Wochenraster einer Freizeit und ihre Programmpunkte; den
/// Autosave von Titel und Uhrzeit übernimmt der Autosave-Baustein mit <see cref="PunktPfad"/>. Story 3-2: das Material an den Programmpunkten;
/// den Autosave von Bezeichnung und Menge übernimmt der Autosave-Baustein mit <see cref="MaterialPfad"/>. Story 3-5: die Aufgaben einer Freizeit
/// (Autosave des Titels mit <see cref="AufgabePfad"/>) und die Aufgaben-Vorlagen einer Organisation (Autosave mit <see cref="VorlagePfad"/>).
/// Story 3-6: die Programmbibliothek (ablegen, lesen und übernehmen in der Freizeit, lesen und entfernen in der Organisation).
/// </summary>
public sealed class ProgrammApiClient(HttpClient http)
{
    /// <summary>Pfad des Wochenrasters einer Freizeit.</summary>
    public static string ProgrammPfad(Guid freizeitId) => $"/api/freizeiten/{freizeitId}/programm";

    /// <summary>Pfad eines Programmpunkts, auch für den Autosave-Baustein (<c>PATCH</c>, AD-19).</summary>
    public static string PunktPfad(Guid freizeitId, Guid punktId) => $"{ProgrammPfad(freizeitId)}/punkte/{punktId}";

    /// <summary>
    /// <c>GET /api/freizeiten/{freizeitId}/programm</c> (Leitung und Team, FR-34): der Wochenraster mit allen Programmpunkten oder <c>null</c> bei
    /// <c>403</c>/<c>404</c>.
    /// </summary>
    public async Task<Wochenraster?> LadenAsync(Guid freizeitId, CancellationToken abbruch = default)
    {
        using var antwort = await http.GetAsync(ProgrammPfad(freizeitId), abbruch);
        if (antwort.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return null;
        }

        antwort.EnsureSuccessStatusCode();
        return await antwort.Content.ReadFromJsonAsync<Wochenraster>(ApiJson.Optionen, abbruch)
               ?? throw new HttpRequestException("Leere Antwort beim Laden des Wochenrasters.");
    }

    /// <summary>
    /// <c>POST …/programm/punkte</c>: angelegt mit dem Programmpunkt, Feldfehler (<c>400</c>, Fehler je JSON-Feldname), abgeschlossene Freizeit
    /// oder ein anderer Fehler.
    /// </summary>
    public async Task<ProgrammAktion> AnlegenAsync(Guid freizeitId, ProgrammpunktNeu punkt, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync($"{ProgrammPfad(freizeitId)}/punkte", punkt, ApiJson.Optionen, abbruch);
        if (antwort.StatusCode == HttpStatusCode.BadRequest)
        {
            return new ProgrammAktion(ProgrammAktionErgebnis.Fehler, null, await ApiFehler.AlleFeldFehlerAsync(antwort, abbruch));
        }

        return await AktionAsync(antwort, abbruch);
    }

    /// <summary><c>POST …/punkte/{punktId}/uebernehmen</c> („Ich übernehme“, FR-35): mit dem Programmpunkt danach.</summary>
    public async Task<ProgrammAktion> UebernehmenAsync(Guid freizeitId, Guid punktId, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsync($"{PunktPfad(freizeitId, punktId)}/uebernehmen", null, abbruch);
        return await AktionAsync(antwort, abbruch);
    }

    /// <summary><c>POST …/punkte/{punktId}/abgeben</c>: mit dem Programmpunkt danach.</summary>
    public async Task<ProgrammAktion> AbgebenAsync(Guid freizeitId, Guid punktId, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsync($"{PunktPfad(freizeitId, punktId)}/abgeben", null, abbruch);
        return await AktionAsync(antwort, abbruch);
    }

    /// <summary>
    /// Story 6-2: <c>POST …/punkte/{punktId}/mitgeteilt</c> („Ja, informieren“ nach dem Senden in „Alle“ oder „Ausblenden“): erledigt, inzwischen
    /// erneut verschoben (<c>409 konflikt</c>), abgeschlossene Freizeit oder ein anderer Fehler.
    /// </summary>
    /// <param name="beginn">Die mitgeteilte Uhrzeit (<c>HH:mm</c>) oder <c>null</c> ohne Uhrzeit.</param>
    public async Task<ProgrammAktionErgebnis> MitgeteiltAsync(Guid freizeitId, Guid punktId, string? beginn, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync($"{PunktPfad(freizeitId, punktId)}/mitgeteilt", new ProgrammpunktMitgeteilt(beginn), ApiJson.Optionen, abbruch);
        return antwort.IsSuccessStatusCode ? ProgrammAktionErgebnis.Erfolg : await FehlerAsync(antwort, abbruch);
    }

    /// <summary><c>DELETE …/punkte/{punktId}</c>: gelöscht, Mahlzeit (<c>409 konflikt</c>), abgeschlossene Freizeit oder ein anderer Fehler.</summary>
    public async Task<ProgrammAktionErgebnis> LoeschenAsync(Guid freizeitId, Guid punktId, CancellationToken abbruch = default)
    {
        using var antwort = await http.DeleteAsync(PunktPfad(freizeitId, punktId), abbruch);
        return antwort.IsSuccessStatusCode ? ProgrammAktionErgebnis.Erfolg : await FehlerAsync(antwort, abbruch);
    }

    /// <summary>Story 3-2: Pfad der Materialliste einer Freizeit.</summary>
    public static string MaterialListePfad(Guid freizeitId) => $"{ProgrammPfad(freizeitId)}/material";

    /// <summary>Story 3-2: Pfad eines Materials, auch für den Autosave-Baustein (<c>PATCH</c>, AD-19).</summary>
    public static string MaterialPfad(Guid freizeitId, Guid materialId) => $"{MaterialListePfad(freizeitId)}/{materialId}";

    /// <summary>
    /// Story 3-2: <c>GET …/programm/material</c> (Leitung und Team, FR-69): das Material aller Programmpunkte oder <c>null</c> bei
    /// <c>403</c>/<c>404</c>.
    /// </summary>
    public async Task<Materialliste?> MaterialLadenAsync(Guid freizeitId, CancellationToken abbruch = default)
    {
        using var antwort = await http.GetAsync(MaterialListePfad(freizeitId), abbruch);
        if (antwort.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return null;
        }

        antwort.EnsureSuccessStatusCode();
        return await antwort.Content.ReadFromJsonAsync<Materialliste>(ApiJson.Optionen, abbruch)
               ?? throw new HttpRequestException("Leere Antwort beim Laden der Materialliste.");
    }

    /// <summary>
    /// Story 3-2: <c>POST …/punkte/{punktId}/material</c>: angelegt mit dem Material, Feldfehler (<c>400</c>, Fehler je JSON-Feldname),
    /// abgeschlossene Freizeit oder ein anderer Fehler.
    /// </summary>
    public async Task<MaterialAktion> MaterialAnlegenAsync(Guid freizeitId, Guid punktId, MaterialNeu material, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync($"{PunktPfad(freizeitId, punktId)}/material", material, ApiJson.Optionen, abbruch);
        if (antwort.StatusCode == HttpStatusCode.BadRequest)
        {
            return new MaterialAktion(ProgrammAktionErgebnis.Fehler, null, await ApiFehler.AlleFeldFehlerAsync(antwort, abbruch));
        }

        return await MaterialAktionAsync(antwort, abbruch);
    }

    /// <summary>Story 3-2: <c>POST …/material/{materialId}/status</c> (FR-36): mit dem Material danach.</summary>
    public async Task<MaterialAktion> MaterialStatusAsync(Guid freizeitId, Guid materialId, MaterialStatus status, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync($"{MaterialPfad(freizeitId, materialId)}/status", new MaterialStatusNeu(status), ApiJson.Optionen, abbruch);
        return await MaterialAktionAsync(antwort, abbruch);
    }

    /// <summary>Story 3-2: <c>POST …/material/{materialId}/mitbringen</c> („Ich bringe das mit“): mit dem Material danach.</summary>
    public async Task<MaterialAktion> MitbringenAsync(Guid freizeitId, Guid materialId, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsync($"{MaterialPfad(freizeitId, materialId)}/mitbringen", null, abbruch);
        return await MaterialAktionAsync(antwort, abbruch);
    }

    /// <summary>Story 3-2: <c>POST …/material/{materialId}/nicht-mitbringen</c> („Doch nicht mitbringen“): mit dem Material danach.</summary>
    public async Task<MaterialAktion> NichtMitbringenAsync(Guid freizeitId, Guid materialId, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsync($"{MaterialPfad(freizeitId, materialId)}/nicht-mitbringen", null, abbruch);
        return await MaterialAktionAsync(antwort, abbruch);
    }

    /// <summary>Story 3-2: <c>DELETE …/material/{materialId}</c>: gelöscht, abgeschlossene Freizeit oder ein anderer Fehler.</summary>
    public async Task<ProgrammAktionErgebnis> MaterialLoeschenAsync(Guid freizeitId, Guid materialId, CancellationToken abbruch = default)
    {
        using var antwort = await http.DeleteAsync(MaterialPfad(freizeitId, materialId), abbruch);
        return antwort.IsSuccessStatusCode ? ProgrammAktionErgebnis.Erfolg : await FehlerAsync(antwort, abbruch);
    }

    /// <summary>Story 3-5: Pfad der Aufgaben einer Freizeit.</summary>
    public static string AufgabenPfad(Guid freizeitId) => $"{ProgrammPfad(freizeitId)}/aufgaben";

    /// <summary>Story 3-5: Pfad einer Aufgabe, auch für den Autosave-Baustein (<c>PATCH</c>, AD-19).</summary>
    public static string AufgabePfad(Guid freizeitId, Guid aufgabeId) => $"{AufgabenPfad(freizeitId)}/{aufgabeId}";

    /// <summary>
    /// Story 3-5: <c>GET …/programm/aufgaben</c> (Leitung und Team, FR-39): alle Aufgaben der Freizeit (beim ersten Laden mit den Vorlagen der
    /// Organisation) oder <c>null</c> bei <c>403</c>/<c>404</c>.
    /// </summary>
    public async Task<List<Aufgabe>?> AufgabenAsync(Guid freizeitId, CancellationToken abbruch = default)
    {
        using var antwort = await http.GetAsync(AufgabenPfad(freizeitId), abbruch);
        if (antwort.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return null;
        }

        antwort.EnsureSuccessStatusCode();
        return await antwort.Content.ReadFromJsonAsync<List<Aufgabe>>(ApiJson.Optionen, abbruch)
               ?? throw new HttpRequestException("Leere Antwort beim Laden der Aufgaben.");
    }

    /// <summary>
    /// Story 3-5: <c>POST …/programm/aufgaben</c>: angelegt mit der Aufgabe, Feldfehler (<c>400</c>, Fehler je JSON-Feldname), abgeschlossene
    /// Freizeit oder ein anderer Fehler.
    /// </summary>
    public async Task<AufgabeAktion> AufgabeAnlegenAsync(Guid freizeitId, AufgabeNeu aufgabe, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync(AufgabenPfad(freizeitId), aufgabe, ApiJson.Optionen, abbruch);
        if (antwort.StatusCode == HttpStatusCode.BadRequest)
        {
            return new AufgabeAktion(ProgrammAktionErgebnis.Fehler, null, await ApiFehler.AlleFeldFehlerAsync(antwort, abbruch));
        }

        return await AufgabeAktionAsync(antwort, abbruch);
    }

    /// <summary>
    /// Story 3-5: <c>PATCH …/aufgaben/{aufgabeId}</c> mit der Phase (AD-19, alter Wert aus <paramref name="aufgabe"/>): gespeichert mit neuer
    /// Version, Konflikt (inzwischen geändert), abgeschlossene Freizeit oder ein anderer Fehler.
    /// </summary>
    public async Task<AufgabePhaseAenderung> AufgabePhaseAendernAsync(Guid freizeitId, Aufgabe aufgabe, AufgabenPhase phase, CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(aufgabe);
        var koerper = new Dictionary<string, object?>
        {
            ["version"] = aufgabe.Version,
            ["alterWert"] = AufgabenFelder.PhaseText(aufgabe.Phase),
            [AufgabenFelder.Phase] = AufgabenFelder.PhaseText(phase),
        };
        using var anfrage = new HttpRequestMessage(HttpMethod.Patch, AufgabePfad(freizeitId, aufgabe.Id))
        {
            Content = JsonContent.Create(koerper, options: ApiJson.Optionen),
        };
        using var antwort = await http.SendAsync(anfrage, abbruch);
        if (!antwort.IsSuccessStatusCode)
        {
            return new AufgabePhaseAenderung(await FehlerAsync(antwort, abbruch), aufgabe.Version);
        }

        using var json = JsonDocument.Parse(await antwort.Content.ReadAsStringAsync(abbruch));
        return new AufgabePhaseAenderung(ProgrammAktionErgebnis.Erfolg, json.RootElement.GetProperty("version").GetUInt32());
    }

    /// <summary>Story 3-5: <c>DELETE …/aufgaben/{aufgabeId}</c>: gelöscht, abgeschlossene Freizeit oder ein anderer Fehler.</summary>
    public async Task<ProgrammAktionErgebnis> AufgabeLoeschenAsync(Guid freizeitId, Guid aufgabeId, CancellationToken abbruch = default)
    {
        using var antwort = await http.DeleteAsync(AufgabePfad(freizeitId, aufgabeId), abbruch);
        return antwort.IsSuccessStatusCode ? ProgrammAktionErgebnis.Erfolg : await FehlerAsync(antwort, abbruch);
    }

    /// <summary>
    /// Story 3-5: <c>POST …/aufgaben/{aufgabeId}/uebernehmen</c>, <c>…/abgeben</c>, <c>…/erledigt</c> oder <c>…/offen</c>: mit der Aufgabe danach;
    /// Konflikt, wenn eine andere Person sie übernommen hat.
    /// </summary>
    public async Task<AufgabeAktion> AufgabeUebergangAsync(Guid freizeitId, Guid aufgabeId, AufgabenUebergang uebergang, CancellationToken abbruch = default)
    {
        var endung = uebergang switch
        {
            AufgabenUebergang.Uebernehmen => "uebernehmen",
            AufgabenUebergang.Abgeben => "abgeben",
            AufgabenUebergang.Erledigt => "erledigt",
            AufgabenUebergang.Offen => "offen",
            _ => throw new ArgumentOutOfRangeException(nameof(uebergang), uebergang, null),
        };
        using var antwort = await http.PostAsync($"{AufgabePfad(freizeitId, aufgabeId)}/{endung}", null, abbruch);
        return await AufgabeAktionAsync(antwort, abbruch);
    }

    /// <summary>Story 3-5: <c>POST …/aufgaben/{aufgabeId}/vorlage</c> („Als Vorlage speichern“): mit der Aufgabe danach (<c>Vorlage = true</c>).</summary>
    public async Task<AufgabeAktion> AufgabeAlsVorlageAsync(Guid freizeitId, Guid aufgabeId, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsync($"{AufgabePfad(freizeitId, aufgabeId)}/vorlage", null, abbruch);
        return await AufgabeAktionAsync(antwort, abbruch);
    }

    /// <summary>Story 3-5: Pfad der Aufgaben-Vorlagen einer Organisation.</summary>
    public static string VorlagenPfad(Guid orgId) => $"/api/organisationen/{orgId}/aufgaben-vorlagen";

    /// <summary>Story 3-5: Pfad einer Aufgaben-Vorlage, auch für den Autosave-Baustein (<c>PATCH</c>, AD-19).</summary>
    public static string VorlagePfad(Guid orgId, Guid vorlageId) => $"{VorlagenPfad(orgId)}/{vorlageId}";

    /// <summary>Story 3-5: <c>GET /api/organisationen/{orgId}/aufgaben-vorlagen</c> (Org-Admin): die Vorlagen oder <c>null</c> bei <c>403</c>/<c>404</c>.</summary>
    public async Task<List<AufgabenVorlage>?> VorlagenAsync(Guid orgId, CancellationToken abbruch = default)
    {
        using var antwort = await http.GetAsync(VorlagenPfad(orgId), abbruch);
        if (antwort.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return null;
        }

        antwort.EnsureSuccessStatusCode();
        return await antwort.Content.ReadFromJsonAsync<List<AufgabenVorlage>>(ApiJson.Optionen, abbruch)
               ?? throw new HttpRequestException("Leere Antwort beim Laden der Aufgaben-Vorlagen.");
    }

    /// <summary>Story 3-5: <c>POST …/aufgaben-vorlagen</c>: angelegt mit der Vorlage, Feldfehler (<c>400</c>) oder ein anderer Fehler.</summary>
    public async Task<VorlageAktion> VorlageAnlegenAsync(Guid orgId, AufgabenVorlageNeu vorlage, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync(VorlagenPfad(orgId), vorlage, ApiJson.Optionen, abbruch);
        if (antwort.StatusCode == HttpStatusCode.BadRequest)
        {
            return new VorlageAktion(ProgrammAktionErgebnis.Fehler, null, await ApiFehler.AlleFeldFehlerAsync(antwort, abbruch));
        }

        if (!antwort.IsSuccessStatusCode)
        {
            return new VorlageAktion(await FehlerAsync(antwort, abbruch), null, new Dictionary<string, string>());
        }

        var neu = await antwort.Content.ReadFromJsonAsync<AufgabenVorlage>(ApiJson.Optionen, abbruch)
                  ?? throw new HttpRequestException("Leere Antwort zur Aufgaben-Vorlage.");
        return new VorlageAktion(ProgrammAktionErgebnis.Erfolg, neu, new Dictionary<string, string>());
    }

    /// <summary>Story 3-5: <c>DELETE …/aufgaben-vorlagen/{vorlageId}</c>: gelöscht oder ein Fehler.</summary>
    public async Task<ProgrammAktionErgebnis> VorlageLoeschenAsync(Guid orgId, Guid vorlageId, CancellationToken abbruch = default)
    {
        using var antwort = await http.DeleteAsync(VorlagePfad(orgId, vorlageId), abbruch);
        return antwort.IsSuccessStatusCode ? ProgrammAktionErgebnis.Erfolg : await FehlerAsync(antwort, abbruch);
    }

    /// <summary>Story 3-6: <c>POST …/punkte/{punktId}/bibliothek</c> („In Bibliothek ablegen“): mit dem Programmpunkt danach (<c>InBibliothek = true</c>).</summary>
    public async Task<ProgrammAktion> AblegenAsync(Guid freizeitId, Guid punktId, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsync($"{PunktPfad(freizeitId, punktId)}/bibliothek", null, abbruch);
        return await AktionAsync(antwort, abbruch);
    }

    /// <summary>Story 3-6: Pfad der Programmbibliothek in einer Freizeit.</summary>
    public static string BibliothekPfad(Guid freizeitId) => $"{ProgrammPfad(freizeitId)}/bibliothek";

    /// <summary>
    /// Story 3-6: <c>GET …/programm/bibliothek</c> (Leitung und Team, FR-41): die Einträge der Programmbibliothek der Organisation oder <c>null</c>
    /// bei <c>403</c>/<c>404</c>.
    /// </summary>
    public Task<List<BibliotheksEintrag>?> BibliothekAsync(Guid freizeitId, CancellationToken abbruch = default) =>
        EintraegeAsync(BibliothekPfad(freizeitId), abbruch);

    /// <summary>
    /// Story 3-6: <c>POST …/programm/bibliothek/{eintragId}/uebernehmen</c>: übernommen mit dem neuen Programmpunkt, Feldfehler (<c>400</c>, Fehler je
    /// JSON-Feldname), abgeschlossene Freizeit oder ein anderer Fehler.
    /// </summary>
    public async Task<ProgrammAktion> BibliothekUebernehmenAsync(Guid freizeitId, Guid eintragId, BibliotheksUebernahme uebernahme, CancellationToken abbruch = default)
    {
        using var antwort = await http.PostAsJsonAsync($"{BibliothekPfad(freizeitId)}/{eintragId}/uebernehmen", uebernahme, ApiJson.Optionen, abbruch);
        if (antwort.StatusCode == HttpStatusCode.BadRequest)
        {
            return new ProgrammAktion(ProgrammAktionErgebnis.Fehler, null, await ApiFehler.AlleFeldFehlerAsync(antwort, abbruch));
        }

        return await AktionAsync(antwort, abbruch);
    }

    /// <summary>Story 3-6: Pfad der Programmbibliothek einer Organisation.</summary>
    public static string OrgBibliothekPfad(Guid orgId) => $"/api/organisationen/{orgId}/programmbibliothek";

    /// <summary>
    /// Story 3-6: <c>GET /api/organisationen/{orgId}/programmbibliothek</c> (Org-Admin): die Einträge oder <c>null</c> bei <c>403</c>/<c>404</c>.
    /// </summary>
    public Task<List<BibliotheksEintrag>?> OrgBibliothekAsync(Guid orgId, CancellationToken abbruch = default) =>
        EintraegeAsync(OrgBibliothekPfad(orgId), abbruch);

    /// <summary>Story 3-6: <c>DELETE …/programmbibliothek/{eintragId}</c>: entfernt oder ein Fehler.</summary>
    public async Task<ProgrammAktionErgebnis> EintragLoeschenAsync(Guid orgId, Guid eintragId, CancellationToken abbruch = default)
    {
        using var antwort = await http.DeleteAsync($"{OrgBibliothekPfad(orgId)}/{eintragId}", abbruch);
        return antwort.IsSuccessStatusCode ? ProgrammAktionErgebnis.Erfolg : await FehlerAsync(antwort, abbruch);
    }

    private async Task<List<BibliotheksEintrag>?> EintraegeAsync(string pfad, CancellationToken abbruch)
    {
        using var antwort = await http.GetAsync(pfad, abbruch);
        if (antwort.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return null;
        }

        antwort.EnsureSuccessStatusCode();
        return await antwort.Content.ReadFromJsonAsync<List<BibliotheksEintrag>>(ApiJson.Optionen, abbruch)
               ?? throw new HttpRequestException("Leere Antwort beim Laden der Programmbibliothek.");
    }

    private static async Task<AufgabeAktion> AufgabeAktionAsync(HttpResponseMessage antwort, CancellationToken abbruch)
    {
        if (!antwort.IsSuccessStatusCode)
        {
            return AufgabeAktion.Ohne(await FehlerAsync(antwort, abbruch));
        }

        var aufgabe = await antwort.Content.ReadFromJsonAsync<Aufgabe>(ApiJson.Optionen, abbruch)
                      ?? throw new HttpRequestException("Leere Antwort zur Aufgabe.");
        return new AufgabeAktion(ProgrammAktionErgebnis.Erfolg, aufgabe, new Dictionary<string, string>());
    }

    private static async Task<MaterialAktion> MaterialAktionAsync(HttpResponseMessage antwort, CancellationToken abbruch)
    {
        if (!antwort.IsSuccessStatusCode)
        {
            return MaterialAktion.Ohne(await FehlerAsync(antwort, abbruch));
        }

        var material = await antwort.Content.ReadFromJsonAsync<Material>(ApiJson.Optionen, abbruch)
                       ?? throw new HttpRequestException("Leere Antwort zum Material.");
        return new MaterialAktion(ProgrammAktionErgebnis.Erfolg, material, new Dictionary<string, string>());
    }

    private static async Task<ProgrammAktion> AktionAsync(HttpResponseMessage antwort, CancellationToken abbruch)
    {
        if (!antwort.IsSuccessStatusCode)
        {
            return ProgrammAktion.Ohne(await FehlerAsync(antwort, abbruch));
        }

        var punkt = await antwort.Content.ReadFromJsonAsync<Programmpunkt>(ApiJson.Optionen, abbruch)
                    ?? throw new HttpRequestException("Leere Antwort zum Programmpunkt.");
        return new ProgrammAktion(ProgrammAktionErgebnis.Erfolg, punkt, new Dictionary<string, string>());
    }

    /// <summary>Ordnet eine Fehlerantwort zu: <c>409</c> nach Problemcode, alles andere ist ein Fehler.</summary>
    private static async Task<ProgrammAktionErgebnis> FehlerAsync(HttpResponseMessage antwort, CancellationToken abbruch)
    {
        if (antwort.StatusCode != HttpStatusCode.Conflict)
        {
            return ProgrammAktionErgebnis.Fehler;
        }

        return await CodeAsync(antwort, abbruch) switch
        {
            Problemcodes.Konflikt => ProgrammAktionErgebnis.Konflikt,
            Problemcodes.FreizeitAbgeschlossen => ProgrammAktionErgebnis.Abgeschlossen,
            _ => ProgrammAktionErgebnis.Fehler,
        };
    }

    /// <summary>Der Problemcode einer ProblemDetails-Antwort oder <c>null</c>.</summary>
    private static async Task<string?> CodeAsync(HttpResponseMessage antwort, CancellationToken abbruch)
    {
        try
        {
            using var problem = JsonDocument.Parse(await antwort.Content.ReadAsStringAsync(abbruch));
            return problem.RootElement.ValueKind == JsonValueKind.Object
                   && problem.RootElement.TryGetProperty(Problemcodes.Feld, out var code)
                   && code.ValueKind == JsonValueKind.String
                ? code.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
