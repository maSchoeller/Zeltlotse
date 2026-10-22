using System.Globalization;
using System.Text.Json;
using Microsoft.Playwright;
using static Zeltlotse.E2E.Tests.E2EHilfe;

namespace Zeltlotse.E2E.Tests;

/// <summary>
/// Story 6-5: Baut über die echten Endpunkte eine gerade laufende Freizeit für die Offline-Tests auf (Muster <see cref="NotfallTests"/>; die Demo-Freizeit
/// liegt 2027). Andrea (Org-Admin der Demo-Organisation) legt sie vom Vortag bis in drei Tagen an und lädt eine eigene Leitung ein (nie Jonas, damit
/// andere Bilder unverändert bleiben). Die Leitung meldet sich in ihrer Seite an, setzt ihren Namen, nimmt an, richtet die Sicherheitskette ein, fragt
/// Allergien, Medikamente, Notfallkontakte und Krankheiten ab und schaltet Verpflegung an. Eine Familie (eigenes Konto) legt ein Kind an, beantwortet
/// den Freizeitpass und meldet es an; die Leitung bestätigt, bis das Kind auf der Notfallkarte steht. Alle Namen sind erfunden.
/// </summary>
internal sealed class LaufendeFreizeit(VeroeffentlichterHost host)
{
    private const string AndreaMail = "andrea.beispiel@example.org";

    public const string LeitungTelefon = "0123 456792";

    public const string Kind = "Paula";

    public const string Familienname = "Sommer";

    public const string Medikament = "Salbutamol-Spray bei Atemnot, 2 Hübe";

    public Guid FreizeitId { get; private set; }

    public string Name { get; private set; } = "";

    public DateOnly LetzterTag { get; private set; }

    /// <summary>Baut alles auf; die Leitung ist danach in <paramref name="seite"/> angemeldet (auf ihrer Startseite).</summary>
    public async Task AufbauenAsync(IBrowserContext kontext, IPage seite, string name, string leitungMail, string elternMail)
    {
        Name = name;
        await FreizeitAnlegenAsync(name, leitungMail);

        var einladung = (await host.Mails.WartenAufAsync(leitungMail, 1, TimeSpan.FromSeconds(60))).Link;
        await PerLinkAnmeldenAsync(seite, host, leitungMail);
        await NameSetzenAsync(kontext, "Nora", "Notfall");
        await seite.GotoAsync(einladung);
        await seite.GetByRole(AriaRole.Button, new() { Name = "Einladung annehmen" }).ClickAsync();
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync(name, new() { Timeout = 60_000 });
        foreach (var (feld, wert) in new[]
                 {
                     ("wasTun", "Ruhe bewahren.\nBei Lebensgefahr sofort den Notruf 112 wählen.\nLeitung anrufen."),
                     ("leitungTelefon", LeitungTelefon),
                     ("heimatName", "Gemeindebüro"),
                     ("heimatTelefon", "0123 456793"),
                 })
        {
            await SendenAsync(kontext, "PATCH", $"/api/freizeiten/{FreizeitId}/sicherheit/sicherheitskette", new Dictionary<string, object?> { ["version"] = 0, ["alterWert"] = null, [feld] = wert });
        }

        var fragen = await LesenAsync(kontext, $"/api/freizeiten/{FreizeitId}/fragen");
        await SendenAsync(kontext, "PATCH", $"/api/freizeiten/{FreizeitId}/fragen", new Dictionary<string, object?>
        {
            ["version"] = fragen.GetProperty("version").GetUInt32(),
            ["alterWert"] = fragen.GetProperty("teilnehmende").GetString(),
            ["teilnehmende"] = "allergien,medikamente,notfallkontakte,krankheiten",
        });
        var module = await LesenAsync(kontext, $"/api/freizeiten/{FreizeitId}/module");
        await SendenAsync(kontext, "PATCH", $"/api/freizeiten/{FreizeitId}/module", new Dictionary<string, object?>
        {
            ["version"] = module.GetProperty("version").GetUInt32(),
            ["alterWert"] = module.GetProperty("verpflegung").GetBoolean() ? "true" : "false",
            ["verpflegung"] = "true",
        });

        var anmeldelink = (await LesenAsync(kontext, $"/api/freizeiten/{FreizeitId}/freizeitseite")).GetProperty("anmeldelinks").EnumerateArray()
            .Single(l => l.GetProperty("art").GetString() == "Teilnehmende").GetProperty("adresse").GetString()!;
        var anmeldung = await FamilieAnmeldenAsync(elternMail, Token(anmeldelink));
        await SendenAsync(kontext, "POST", $"/api/freizeiten/{FreizeitId}/anmeldungen/{anmeldung}/bestaetigen", null);
        await AufDerNotfallkarteAsync(kontext);
    }

    private async Task FreizeitAnlegenAsync(string name, string leitungMail)
    {
        var kontext = await host.KontextAsync(new BrowserNewContextOptions { Locale = "de-DE", TimezoneId = "Europe/Berlin" });
        await using var _ = kontext;
        var seite = await kontext.NewPageAsync();
        await PerLinkAnmeldenAsync(seite, host, AndreaMail);
        var organisation = (await LesenAsync(kontext, "/api/ich/organisationen")).EnumerateArray()
            .Single(o => o.GetProperty("rollen").EnumerateArray().Any(r => r.GetString() == "OrgAdmin"))
            .GetProperty("organisationId").GetGuid();
        var heute = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")).DateTime);
        LetzterTag = heute.AddDays(3);
        var angelegt = await SendenAsync(
            kontext, "POST", $"/api/organisationen/{organisation}/freizeiten",
            new { name, ersterTag = heute.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), letzterTag = LetzterTag.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
        FreizeitId = angelegt.GetProperty("id").GetGuid();
        await SendenAsync(kontext, "POST", $"/api/organisationen/{organisation}/freizeiten/{FreizeitId}/leitung/einladungen", new { mail = leitungMail });
    }

    /// <summary>Eine Familie mit eigenem Konto legt das Kind an, beantwortet den Pass und meldet es an; liefert die Kennung der Anmeldung.</summary>
    private async Task<Guid> FamilieAnmeldenAsync(string elternMail, string anmeldelinkToken)
    {
        var kontext = await host.KontextAsync(new BrowserNewContextOptions { Locale = "de-DE", TimezoneId = "Europe/Berlin" });
        await using var _ = kontext;
        var seite = await kontext.NewPageAsync();
        await PerLinkAnmeldenAsync(seite, host, elternMail);
        await NameSetzenAsync(kontext, "Karin", Familienname);
        var kind = (await SendenAsync(kontext, "POST", "/api/ich/familie/kinder", new { vorname = Kind, nachname = Familienname, geburtsdatum = "2015-06-12" }))
            .GetProperty("personId").GetGuid();
        foreach (var (kennung, wert) in new[]
                 {
                     ("allergien", "milch"), ("medikamente", Medikament), ("notfallkontakte", "Karin Sommer (Mutter), 0123 456794"),
                 })
        {
            await SendenAsync(kontext, "PATCH", $"/api/ich/freizeitpass/{kind}/antworten/{kennung}", new Dictionary<string, object?> { ["version"] = 0, ["alterWert"] = null, ["wert"] = wert });
        }

        return (await SendenAsync(kontext, "POST", "/api/ich/anmeldungen", new { anmeldelinkToken, personId = kind, teilnahmebedingungenBestaetigt = true }))
            .GetProperty("anmeldungId").GetGuid();
    }

    /// <summary>Wartet, bis die Freigabe der Stufe Team zugestellt ist (Outbox) und das Kind auf der Notfallkarte steht.</summary>
    private async Task AufDerNotfallkarteAsync(IBrowserContext kontext)
    {
        for (var versuch = 0; versuch < 120; versuch++)
        {
            if ((await LesenAsync(kontext, $"/api/freizeiten/{FreizeitId}/personen/notfallkarte")).EnumerateArray().Any(p => p.GetProperty("vorname").GetString() == Kind))
            {
                return;
            }

            await Task.Delay(500, TestContext.Current.CancellationToken);
        }

        Assert.Fail("Das Kind steht nicht auf der Notfallkarte.");
    }

    /// <summary>
    /// Liest den Offline-Speicher der Seite roh aus IndexedDB, ohne ihn anzulegen: Schlüssel der Datensätze, ihr Inhalt als Text (so, wie er gespeichert ist)
    /// und die Zahl der Geräte-Schlüssel. Ohne Datenbank alles leer.
    /// </summary>
    public const string OfflineRohJs =
        """
        async () => {
            const leer = { schluessel: [], roh: '', geraet: 0 };
            if (!(await indexedDB.databases()).some(d => d.name === 'zeltlotse-offline')) return leer;
            const db = await new Promise((ok, fehler) => { const r = indexedDB.open('zeltlotse-offline'); r.onsuccess = () => ok(r.result); r.onerror = () => fehler(r.error); });
            try {
                if (!db.objectStoreNames.contains('daten')) return leer;
                const lesen = (ablage, art) => new Promise(ok => { const r = db.transaction(ablage).objectStore(ablage)[art](); r.onsuccess = () => ok(r.result); });
                const alle = await lesen('daten', 'getAll');
                const roh = alle.map(d => d.schluessel + ' ' + d.freizeitId + ' ' + new TextDecoder().decode(d.inhalt) + ' ' + new TextDecoder('latin1').decode(d.inhalt)).join(String.fromCharCode(10));
                return { schluessel: alle.map(d => d.schluessel), roh, geraet: await lesen('schluessel', 'count') };
            } finally {
                db.close();
            }
        }
        """;

    /// <summary>Wartet, bis der Offline-Abgleich der Web-App Notfallkarte und alle fünf Tagesansichten dieser Freizeit in IndexedDB abgelegt hat.</summary>
    public async Task GespeichertAsync(IPage seite)
    {
        var freizeit = FreizeitId.ToString("D");
        for (var versuch = 0; versuch < 120; versuch++)
        {
            var schluessel = await SchluesselAsync(seite);
            if (schluessel.Contains($"{freizeit}/notfallkarte") && schluessel.Count(s => s.StartsWith($"{freizeit}/tagesansicht/", StringComparison.Ordinal)) == 5)
            {
                return;
            }

            await Task.Delay(500, TestContext.Current.CancellationToken);
        }

        Assert.Fail("Der Offline-Abgleich hat die Freizeit nicht gespeichert.");
    }

    /// <summary>
    /// Legt über das Modul der Web-App eine zweite, nicht gelistete Freizeit an: Kopf und Notfallkarte als Kopie der eigenen (Kennung ersetzt) und einen
    /// wartenden Vorfall-Eintrag. Argumente: eigene Freizeit-Kennung, fremde Freizeit-Kennung, Kennung des Eintrags.
    /// </summary>
    public const string FremdeFreizeitAnlegenJs =
        """
        async ([eigen, fremd, eintrag]) => {
            const m = await import(new URL('js/zeltlotse.js', document.baseURI).href);
            const ablauf = Date.now() + 3 * 24 * 60 * 60 * 1000;
            const kopf = (await m.offlineLesen(eigen + '/freizeit')).split(eigen).join(fremd);
            const karte = await m.offlineLesen(eigen + '/notfallkarte');
            await m.offlineSchreiben(fremd + '/freizeit', fremd, ablauf, kopf);
            await m.offlineSchreiben(fremd + '/notfallkarte', fremd, ablauf, karte);
            await m.offlineSchreiben('vorfall/' + fremd + '/' + eintrag, fremd, ablauf,
                JSON.stringify({ id: eintrag, personId: crypto.randomUUID(), art: 1, text: 'Probe einer fremden Freizeit' }));
        }
        """;

    /// <summary>Wartet, bis der Offline-Abgleich alle Datensätze der Freizeit <paramref name="freizeitId"/> gelöscht hat, auch die wartenden Einträge.</summary>
    public static async Task OhneFreizeitAsync(IPage seite, Guid freizeitId)
    {
        var freizeit = freizeitId.ToString("D");
        for (var versuch = 0; versuch < 120; versuch++)
        {
            if (!(await SchluesselAsync(seite)).Any(s => s.StartsWith(freizeit, StringComparison.Ordinal) || s.StartsWith($"vorfall/{freizeit}/", StringComparison.Ordinal)))
            {
                return;
            }

            await Task.Delay(500, TestContext.Current.CancellationToken);
        }

        Assert.Fail("Der Offline-Abgleich hat die nicht mehr gelistete Freizeit nicht gelöscht.");
    }

    /// <summary>Die Schlüssel der Datensätze im Offline-Speicher der Seite.</summary>
    public static async Task<List<string>> SchluesselAsync(IPage seite) =>
        [.. (await seite.EvaluateAsync<JsonElement>(OfflineRohJs)).GetProperty("schluessel").EnumerateArray().Select(s => s.GetString()!)];

    /// <summary>Setzt Vor- und Nachname im eigenen Profil (Autosave-PATCH, AD-19).</summary>
    private async Task NameSetzenAsync(IBrowserContext kontext, string vorname, string nachname)
    {
        var version = (await SendenAsync(kontext, "POST", "/api/ich/profil", null)).GetProperty("version").GetUInt32();
        version = (await SendenAsync(kontext, "PATCH", "/api/ich/profil", new Dictionary<string, object?> { ["version"] = version, ["alterWert"] = null, ["vorname"] = vorname }))
            .GetProperty("version").GetUInt32();
        await SendenAsync(kontext, "PATCH", "/api/ich/profil", new Dictionary<string, object?> { ["version"] = version, ["alterWert"] = null, ["nachname"] = nachname });
    }

    private string Adresse(string pfad) => new Uri(host.Adresse, pfad).ToString();

    public async Task<JsonElement> LesenAsync(IBrowserContext kontext, string pfad)
    {
        var antwort = await kontext.APIRequest.GetAsync(Adresse(pfad));
        Assert.True(antwort.Ok, $"GET {pfad}: {antwort.Status}");
        return (await antwort.JsonAsync())!.Value;
    }

    /// <summary>Ändernder Aufruf mit der Sitzung des Kontexts und frisch geholtem Antiforgery-Token (AD-25); liefert die Antwort als JSON.</summary>
    private async Task<JsonElement> SendenAsync(IBrowserContext kontext, string methode, string pfad, object? koerper)
    {
        var token = (await LesenAsync(kontext, "/api/betrieb/antiforgery")).GetProperty("token").GetString()!;
        var antwort = await kontext.APIRequest.FetchAsync(Adresse(pfad), new()
        {
            Method = methode,
            DataObject = koerper,
            Headers = new Dictionary<string, string> { ["X-Zeltlotse-Antiforgery"] = token },
        });
        Assert.True(antwort.Ok, $"{methode} {pfad}: {antwort.Status} {await antwort.TextAsync()}");
        var text = await antwort.TextAsync();
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }
}
