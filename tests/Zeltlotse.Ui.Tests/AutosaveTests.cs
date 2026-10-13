using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Ui.Autosave;

namespace Zeltlotse.Ui.Tests;

/// <summary>Autosave nach AD-19 (Matrix-Zeilen „Autosave gespeichert/Konflikt/gescheitert“): Feld und Speicher-Dienst.</summary>
public sealed class AutosaveTests : BunitContext
{
    private const string Pfad = "/api/freizeiten/0190a0f0-0000-7000-8000-000000000001/programm/punkte/0190a0f0-0000-7000-8000-000000000002";
    private readonly NachgebildeterHandler _server = new();

    public AutosaveTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddScoped(_ => new AutosaveDienst(_server.Client()));
    }

    private IRenderedComponent<AutosaveFeld> Feld(string feldname = "titel", string wert = "Geländespiel", uint version = 7, Action? neuLaden = null) =>
        Render<AutosaveFeld>(ps => ps
            .Add(p => p.Label, "Titel")
            .Add(p => p.Feldname, feldname)
            .Add(p => p.Pfad, Pfad)
            .Add(p => p.Wert, wert)
            .Add(p => p.Version, version)
            .Add(p => p.Hinweis, "Nur der Titel ist Pflicht.")
            .Add(p => p.NeuLaden, () => neuLaden?.Invoke()));

    private static void Aendern(IRenderedComponent<AutosaveFeld> feld, string wert)
    {
        var eingabe = feld.Find("input");
        eingabe.Focus();
        eingabe.Input(wert);
        eingabe.Blur();
    }

    private static Func<HttpResponseMessage> Gespeichert(uint version, string feld, string wert) =>
        () => NachgebildeterHandler.Json(HttpStatusCode.OK, JsonSerializer.Serialize(new Dictionary<string, object> { ["version"] = version, [feld] = wert }));

    private static JsonElement Koerper(Aufgezeichnet anfrage) => JsonDocument.Parse(anfrage.Koerper!).RootElement;

    [Fact]
    public void Verlassen_ohne_Aenderung_speichert_nicht()
    {
        var feld = Feld();

        feld.Find("input").Focus();
        feld.Find("input").Blur();
        feld.Find("input").Input("Geländespiel");
        feld.Find("input").Blur();

        Assert.Empty(_server.Anfragen);
        Assert.Empty(feld.FindAll("[role=status]"));
    }

    [Fact]
    public void Geaendert_und_verlassen_sendet_PATCH_mit_Version_altem_Wert_und_Feld_und_uebernimmt_die_Antwort()
    {
        _server.Bei(HttpMethod.Patch, Pfad, Gespeichert(8, "titel", "Geländespiel Dienstag"));
        var feld = Feld();

        Aendern(feld, "Geländespiel Dienstag ");

        var anfrage = Assert.Single(_server.Anfragen);
        Assert.Equal(HttpMethod.Patch, anfrage.Methode);
        Assert.Equal(Pfad, anfrage.Pfad);
        var koerper = Koerper(anfrage);
        Assert.Equal(3, koerper.EnumerateObject().Count());
        Assert.Equal(7u, koerper.GetProperty("version").GetUInt32());
        Assert.Equal("Geländespiel", koerper.GetProperty("alterWert").GetString());
        Assert.Equal("Geländespiel Dienstag ", koerper.GetProperty("titel").GetString());

        Assert.Equal("Geländespiel Dienstag", feld.Find("input").GetAttribute("value"));
        var hinweis = feld.Find("p[role=status]");
        Assert.StartsWith("Gespeichert · ", hinweis.TextContent, StringComparison.Ordinal);
        Assert.Equal("Rückgängig", feld.Find("p[role=status] button").TextContent);
        Assert.DoesNotContain("Nur der Titel ist Pflicht.", feld.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Rueckgaengig_sendet_PATCH_mit_vertauschten_Werten_und_neuer_Version_danach_Gespeichert_ohne_Rueckgaengig()
    {
        _server.BeiDerReihe(HttpMethod.Patch, Pfad, Gespeichert(8, "titel", "Nachtwanderung"), Gespeichert(9, "titel", "Geländespiel"));
        var feld = Feld();
        Aendern(feld, "Nachtwanderung");

        feld.Find("p[role=status] button").Click();

        Assert.Equal(2, _server.Anfragen.Count);
        var koerper = Koerper(_server.Anfragen[1]);
        Assert.Equal(8u, koerper.GetProperty("version").GetUInt32());
        Assert.Equal("Nachtwanderung", koerper.GetProperty("alterWert").GetString());
        Assert.Equal("Geländespiel", koerper.GetProperty("titel").GetString());
        Assert.Equal("Geländespiel", feld.Find("input").GetAttribute("value"));
        Assert.Equal("Gespeichert", feld.Find("p[role=status]").TextContent);
        Assert.Empty(feld.FindAll("button"));
    }

    [Fact]
    public void Konflikt_zeigt_Inzwischen_geaendert_mit_Neu_laden_das_NeuLaden_ausloest()
    {
        _server.Bei(HttpMethod.Patch, Pfad, () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.Konflikt));
        var neuLaden = 0;
        var feld = Feld(neuLaden: () => neuLaden++);

        Aendern(feld, "Nachtwanderung");

        var hinweis = feld.Find("p[role=status]");
        Assert.StartsWith("Inzwischen geändert – neu laden?", hinweis.TextContent, StringComparison.Ordinal);
        var knopf = feld.Find("p[role=status] button");
        Assert.Equal("Neu laden", knopf.TextContent);
        knopf.Click();
        Assert.Equal(1, neuLaden);
    }

    [Fact]
    public void Nach_Neu_laden_uebernimmt_das_Feld_Wert_und_Version_von_aussen()
    {
        _server.BeiDerReihe(HttpMethod.Patch, Pfad,
            () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.Konflikt),
            Gespeichert(12, "titel", "Fackelwanderung"));
        var feld = Feld();
        Aendern(feld, "Nachtwanderung");

        feld.Render(ps => ps.Add(p => p.Wert, "Schnitzeljagd").Add(p => p.Version, 11u));
        Aendern(feld, "Fackelwanderung");

        Assert.Equal("Fackelwanderung", feld.Find("input").GetAttribute("value"));
        var koerper = Koerper(_server.Anfragen[1]);
        Assert.Equal(11u, koerper.GetProperty("version").GetUInt32());
        Assert.Equal("Schnitzeljagd", koerper.GetProperty("alterWert").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void Serverfehler_zeigt_Nicht_gespeichert_mit_Erneut_versuchen_und_der_Wert_bleibt(HttpStatusCode status)
    {
        _server.Bei(HttpMethod.Patch, Pfad, () => NachgebildeterHandler.Problem(status, "unerwarteter-fehler"));
        var feld = Feld();

        Aendern(feld, "Nachtwanderung");

        var hinweis = feld.Find("p[role=alert]");
        Assert.StartsWith("Nicht gespeichert.", hinweis.TextContent, StringComparison.Ordinal);
        Assert.Equal("Erneut versuchen", feld.Find("p[role=alert] button").TextContent);
        Assert.Equal("Nachtwanderung", feld.Find("input").GetAttribute("value"));
    }

    [Fact]
    public void Netzfehler_zeigt_Nicht_gespeichert_und_der_erneute_Versuch_sendet_denselben_Koerper()
    {
        var versuche = 0;
        _server.Bei(r => r.Method == HttpMethod.Patch, _ => ++versuche == 1
            ? throw new HttpRequestException("Kein Netz")
            : Task.FromResult(Gespeichert(8, "titel", "Nachtwanderung")()));
        var feld = Feld();
        Aendern(feld, "Nachtwanderung");
        Assert.StartsWith("Nicht gespeichert.", feld.Find("p[role=alert]").TextContent, StringComparison.Ordinal);
        Assert.Equal("Nachtwanderung", feld.Find("input").GetAttribute("value"));

        feld.Find("p[role=alert] button").Click();

        Assert.Equal(2, _server.Anfragen.Count);
        Assert.Equal(_server.Anfragen[0].Koerper, _server.Anfragen[1].Koerper);
        Assert.StartsWith("Gespeichert", feld.Find("p[role=status]").TextContent, StringComparison.Ordinal);
        Assert.Equal("Rückgängig", feld.Find("p[role=status] button").TextContent);
    }

    [Fact]
    public void Erneut_versuchen_nach_gescheitertem_Rueckgaengig_sendet_das_Rueckgaengig_unveraendert()
    {
        var versuche = 0;
        _server.Bei(r => r.Method == HttpMethod.Patch, _ => ++versuche switch
        {
            1 => Task.FromResult(Gespeichert(8, "titel", "Nachtwanderung")()),
            2 => Task.FromResult(NachgebildeterHandler.Problem(HttpStatusCode.InternalServerError, "unerwarteter-fehler")),
            _ => Task.FromResult(Gespeichert(9, "titel", "Geländespiel")()),
        });
        var feld = Feld();
        Aendern(feld, "Nachtwanderung");
        feld.Find("p[role=status] button").Click();
        Assert.StartsWith("Nicht gespeichert.", feld.Find("p[role=alert]").TextContent, StringComparison.Ordinal);

        feld.Find("p[role=alert] button").Click();

        Assert.Equal(3, _server.Anfragen.Count);
        Assert.Equal(_server.Anfragen[1].Koerper, _server.Anfragen[2].Koerper);
        var koerper = Koerper(_server.Anfragen[2]);
        Assert.Equal("Nachtwanderung", koerper.GetProperty("alterWert").GetString());
        Assert.Equal("Geländespiel", koerper.GetProperty("titel").GetString());
        Assert.Equal("Gespeichert", feld.Find("p[role=status]").TextContent);
        Assert.Empty(feld.FindAll("button"));
    }

    [Fact]
    public void Waehrend_der_Uebertragung_ist_der_Knopf_deaktiviert()
    {
        var antwort = new TaskCompletionSource<HttpResponseMessage>();
        var versuche = 0;
        _server.Bei(r => r.Method == HttpMethod.Patch, _ => ++versuche == 1
            ? Task.FromResult(NachgebildeterHandler.Problem(HttpStatusCode.InternalServerError, "unerwarteter-fehler"))
            : antwort.Task);
        var feld = Feld();
        Aendern(feld, "Nachtwanderung");

        feld.Find("p[role=alert] button").Click();

        Assert.True(feld.Find("p[role=alert] button").HasAttribute("disabled"));
        antwort.SetResult(Gespeichert(8, "titel", "Nachtwanderung")());
        feld.WaitForAssertion(() => Assert.False(feld.Find("p[role=status] button").HasAttribute("disabled")));
    }

    [Fact]
    public void Hinweis_verschwindet_wenn_ein_anderes_Feld_desselben_Dienstes_den_Fokus_bekommt()
    {
        _server.Bei(HttpMethod.Patch, Pfad, Gespeichert(8, "titel", "Nachtwanderung"));
        var erstes = Feld();
        var zweites = Feld(feldname: "ort", wert: "Wald");
        Aendern(erstes, "Nachtwanderung");
        Assert.NotEmpty(erstes.FindAll("p[role=status]"));

        zweites.Find("input").Focus();

        Assert.Empty(erstes.FindAll("p[role=status]"));
        Assert.Contains("Nur der Titel ist Pflicht.", erstes.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Hinweis_verschwindet_wenn_ein_anderes_Feld_speichert_und_bleibt_beim_eigenen_Fokus()
    {
        _server.Bei(HttpMethod.Patch, Pfad, Gespeichert(8, "titel", "Nachtwanderung"));
        _server.Bei(r => r.Method == HttpMethod.Patch && r.Content!.ReadAsStringAsync().Result.Contains("\"ort\"", StringComparison.Ordinal),
            _ => Task.FromResult(Gespeichert(9, "ort", "See")()));
        var erstes = Feld();
        var zweites = Feld(feldname: "ort", wert: "Wald");
        Aendern(erstes, "Nachtwanderung");

        erstes.Find("input").Focus();
        Assert.NotEmpty(erstes.FindAll("p[role=status]"));

        var eingabe = zweites.Find("input");
        eingabe.Input("See");
        eingabe.Blur();

        Assert.Empty(erstes.FindAll("p[role=status]"));
        Assert.NotEmpty(zweites.FindAll("p[role=status]"));
    }

    [Fact]
    public void Zeitueberschreitung_zeigt_Nicht_gespeichert_und_beendet_die_Uebertragung()
    {
        _server.Bei(r => r.Method == HttpMethod.Patch, _ => throw new TaskCanceledException("Zeitlimit", new TimeoutException()));
        var feld = Feld();

        Aendern(feld, "Nachtwanderung");

        Assert.StartsWith("Nicht gespeichert.", feld.Find("p[role=alert]").TextContent, StringComparison.Ordinal);
        Assert.False(feld.Find("p[role=alert] button").HasAttribute("disabled"));
        Assert.Equal("Nachtwanderung", feld.Find("input").GetAttribute("value"));
    }

    [Fact]
    public void Jede_andere_Ausnahme_beim_Speichern_zeigt_Nicht_gespeichert_statt_die_Fehlergrenze_auszuloesen()
    {
        _server.Bei(r => r.Method == HttpMethod.Patch, _ => throw new InvalidOperationException("unerwartet"));
        var feld = Feld();

        Aendern(feld, "Nachtwanderung");

        Assert.StartsWith("Nicht gespeichert.", feld.Find("p[role=alert]").TextContent, StringComparison.Ordinal);
        Assert.False(feld.Find("p[role=alert] button").HasAttribute("disabled"));
    }

    [Fact]
    public void Waehrend_der_Uebertragung_geaenderter_Wert_bleibt_stehen_und_wird_danach_gespeichert()
    {
        var erste = new TaskCompletionSource<HttpResponseMessage>();
        var versuche = 0;
        _server.Bei(r => r.Method == HttpMethod.Patch, _ => ++versuche == 1
            ? erste.Task
            : Task.FromResult(Gespeichert(9, "titel", "Fackelwanderung")()));
        var feld = Feld();
        Aendern(feld, "Nachtwanderung");

        var eingabe = feld.Find("input");
        eingabe.Input("Fackelwanderung");
        eingabe.Blur();
        erste.SetResult(Gespeichert(8, "titel", "Nachtwanderung")());

        feld.WaitForAssertion(() => Assert.Equal(2, _server.Anfragen.Count));
        var koerper = Koerper(_server.Anfragen[1]);
        Assert.Equal(8u, koerper.GetProperty("version").GetUInt32());
        Assert.Equal("Nachtwanderung", koerper.GetProperty("alterWert").GetString());
        Assert.Equal("Fackelwanderung", koerper.GetProperty("titel").GetString());
        feld.WaitForAssertion(() => Assert.Equal("Fackelwanderung", feld.Find("input").GetAttribute("value")));
    }

    [Fact]
    public void Waehrend_der_Uebertragung_geaenderter_Wert_wird_nicht_von_der_Antwort_ueberschrieben()
    {
        var erste = new TaskCompletionSource<HttpResponseMessage>();
        _server.Bei(r => r.Method == HttpMethod.Patch, _ => erste.Task);
        var feld = Feld();
        Aendern(feld, "Nachtwanderung");

        feld.Find("input").Input("Fackel");
        erste.SetResult(Gespeichert(8, "titel", "Nachtwanderung")());

        feld.WaitForAssertion(() => Assert.NotEmpty(feld.FindAll("p[role=status]")));
        Assert.Equal("Fackel", feld.Find("input").GetAttribute("value"));
        Assert.Single(_server.Anfragen);
    }

    [Fact]
    public void Nicht_gespeichert_bleibt_wenn_ein_anderes_Feld_den_Fokus_bekommt()
    {
        _server.Bei(HttpMethod.Patch, Pfad, () => NachgebildeterHandler.Problem(HttpStatusCode.InternalServerError, "unerwarteter-fehler"));
        var erstes = Feld();
        var zweites = Feld(feldname: "ort", wert: "Wald");
        Aendern(erstes, "Nachtwanderung");

        zweites.Find("input").Focus();

        Assert.StartsWith("Nicht gespeichert.", erstes.Find("p[role=alert]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Konflikt_bleibt_wenn_ein_anderes_Feld_den_Fokus_bekommt()
    {
        _server.Bei(HttpMethod.Patch, Pfad, () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, Problemcodes.Konflikt));
        var erstes = Feld();
        var zweites = Feld(feldname: "ort", wert: "Wald");
        Aendern(erstes, "Nachtwanderung");

        zweites.Find("input").Focus();

        Assert.StartsWith("Inzwischen geändert", erstes.Find("p[role=status]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Erneut_versuchen_nach_einer_Aenderung_sendet_die_aktuelle_Eingabe()
    {
        _server.BeiDerReihe(HttpMethod.Patch, Pfad,
            () => NachgebildeterHandler.Problem(HttpStatusCode.InternalServerError, "unerwarteter-fehler"),
            Gespeichert(8, "titel", "Fackelwanderung"));
        var feld = Feld();
        Aendern(feld, "Nachtwanderung");

        feld.Find("input").Input("Fackelwanderung");
        feld.Find("p[role=alert] button").Click();

        var koerper = Koerper(_server.Anfragen[1]);
        Assert.Equal(7u, koerper.GetProperty("version").GetUInt32());
        Assert.Equal("Geländespiel", koerper.GetProperty("alterWert").GetString());
        Assert.Equal("Fackelwanderung", koerper.GetProperty("titel").GetString());
        Assert.Equal("Fackelwanderung", feld.Find("input").GetAttribute("value"));
    }

    [Fact]
    public async Task Speicher_Dienst_wertet_eine_Zeitueberschreitung_als_gescheitert()
    {
        _server.Bei(r => r.Method == HttpMethod.Patch, _ => throw new TaskCanceledException("Zeitlimit", new TimeoutException()));
        var dienst = new AutosaveDienst(_server.Client());

        var ergebnis = await dienst.SpeichernAsync(new AutosaveAnfrage(Pfad, "titel", 7, "a", "b"), Xunit.TestContext.Current.CancellationToken);

        Assert.IsType<AutosaveErgebnis.Gescheitert>(ergebnis);
    }

    [Fact]
    public async Task Speicher_Dienst_wertet_eine_unvollstaendige_200_Antwort_als_gescheitert()
    {
        _server.Bei(HttpMethod.Patch, Pfad, () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"version":8}"""));
        var dienst = new AutosaveDienst(_server.Client());

        var ergebnis = await dienst.SpeichernAsync(new AutosaveAnfrage(Pfad, "titel", 7, "a", "b"), Xunit.TestContext.Current.CancellationToken);

        Assert.IsType<AutosaveErgebnis.Gescheitert>(ergebnis);
    }

    [Fact]
    public async Task Speicher_Dienst_sendet_null_als_Wert()
    {
        _server.Bei(HttpMethod.Patch, Pfad, () => NachgebildeterHandler.Json(HttpStatusCode.OK, """{"version":8,"ort":null}"""));
        var dienst = new AutosaveDienst(_server.Client());

        var ergebnis = await dienst.SpeichernAsync(new AutosaveAnfrage(Pfad, "ort", 7, "Wald", null), Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(new AutosaveErgebnis.Gespeichert(8, null), ergebnis);
        Assert.Equal("""{"version":7,"alterWert":"Wald","ort":null}""", Assert.Single(_server.Anfragen).Koerper);
    }

    [Fact]
    public void Mehrzeilig_speichert_ein_textarea_beim_Verlassen()
    {
        _server.Bei(HttpMethod.Patch, Pfad, Gespeichert(8, "beschreibung", "Zeile 1\nZeile 2"));
        var feld = Render<AutosaveFeld>(ps => ps
            .Add(p => p.Label, "Beschreibung")
            .Add(p => p.Feldname, "beschreibung")
            .Add(p => p.Pfad, Pfad)
            .Add(p => p.Wert, null)
            .Add(p => p.Version, 7u)
            .Add(p => p.Mehrzeilig, true));

        var eingabe = feld.Find("textarea");
        eingabe.Focus();
        eingabe.Input("Zeile 1\nZeile 2");
        eingabe.Blur();

        var koerper = Koerper(Assert.Single(_server.Anfragen));
        Assert.Equal("Zeile 1\nZeile 2", koerper.GetProperty("beschreibung").GetString());
        Assert.StartsWith("Gespeichert · ", feld.Find("p[role=status]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Gespeichert_meldet_den_gespeicherten_Wert_auch_nach_Rueckgaengig_ein_Konflikt_nicht()
    {
        // Story 3-2: Die Seite führt eine Überschrift mit dem gespeicherten Wert nach (z. B. die Bezeichnung eines Materials).
        var gemeldet = new List<string?>();
        _server.BeiDerReihe(HttpMethod.Patch, Pfad, Gespeichert(8, "titel", "Nachtwanderung"), Gespeichert(9, "titel", "Geländespiel"),
            () => NachgebildeterHandler.Problem(HttpStatusCode.Conflict, "konflikt"));
        var feld = Render<AutosaveFeld>(ps => ps
            .Add(p => p.Label, "Titel")
            .Add(p => p.Feldname, "titel")
            .Add(p => p.Pfad, Pfad)
            .Add(p => p.Wert, "Geländespiel")
            .Add(p => p.Version, 7u)
            .Add(p => p.Gespeichert, (string? wert) => gemeldet.Add(wert)));

        Aendern(feld, "Nachtwanderung ");
        feld.Find("p[role=status] button").Click();
        Aendern(feld, "Abbau");

        Assert.Equal(["Nachtwanderung", "Geländespiel"], gemeldet);
    }
}
