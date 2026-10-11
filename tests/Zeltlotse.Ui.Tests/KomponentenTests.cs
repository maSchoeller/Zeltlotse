using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Zeltlotse.Ui.Komponenten;

namespace Zeltlotse.Ui.Tests;

/// <summary>Baustein-Regeln aus den Design Notes der Story 0-3 für jeden Baustein von Ui.Komponenten (AD-30).</summary>
public sealed class KomponentenTests : BunitContext
{
    public KomponentenTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    // ---------- Knopf ----------

    [Theory]
    [InlineData(KnopfArt.Standard, "elkw-btn elkw-btn--noarrow")]
    [InlineData(KnopfArt.Outline, "elkw-btn elkw-btn--outline elkw-btn--noarrow")]
    [InlineData(KnopfArt.Text, "elkw-btn elkw-btn--text elkw-btn--noarrow")]
    public void Knopf_hat_drei_Arten_und_ist_ohne_Href_ein_button_vom_Typ_button(KnopfArt art, string klassen)
    {
        var cut = Render<Knopf>(ps => ps.Add(p => p.Art, art).AddChildContent("Speichern"));

        var knopf = cut.Find("button");
        Assert.Equal(klassen, knopf.GetAttribute("class"));
        Assert.Equal("button", knopf.GetAttribute("type"));
        Assert.Equal("Speichern", knopf.TextContent);
        Assert.False(knopf.HasAttribute("disabled"));
    }

    [Fact]
    public void Knopf_zeigt_den_Pfeil_nur_bei_Ansichtswechsel()
    {
        var mit = Render<Knopf>(ps => ps.Add(p => p.Ansichtswechsel, true).AddChildContent("Weiter"));
        var ohne = Render<Knopf>(ps => ps.AddChildContent("Ja, senden"));

        Assert.DoesNotContain("elkw-btn--noarrow", mit.Find("button").ClassList);
        Assert.Contains("elkw-btn--noarrow", ohne.Find("button").ClassList);
    }

    [Fact]
    public void Knopf_mit_Href_ist_ein_Link_und_Typ_submit_wird_gesetzt()
    {
        var link = Render<Knopf>(ps => ps.Add(p => p.Href, "programm").Add(p => p.Ansichtswechsel, true).AddChildContent("Programm"));
        var absenden = Render<Knopf>(ps => ps.Add(p => p.Typ, "submit").AddChildContent("Anmeldung absenden"));

        Assert.Equal("programm", link.Find("a").GetAttribute("href"));
        Assert.Empty(link.FindAll("button"));
        Assert.Equal("submit", absenden.Find("button").GetAttribute("type"));
    }

    [Fact]
    public void Knopf_deaktiviert_setzt_disabled_und_als_Link_aria_disabled_ohne_Ziel()
    {
        var knopf = Render<Knopf>(ps => ps.Add(p => p.Deaktiviert, true).AddChildContent("Erneut versuchen"));
        var link = Render<Knopf>(ps => ps.Add(p => p.Deaktiviert, true).Add(p => p.Href, "x").AddChildContent("X"));

        Assert.True(knopf.Find("button").HasAttribute("disabled"));
        Assert.Equal("true", link.Find("a").GetAttribute("aria-disabled"));
        Assert.False(link.Find("a").HasAttribute("href"));
    }

    [Fact]
    public void Knopf_reicht_unbekannte_Attribute_und_onclick_an_das_Element_weiter()
    {
        var geklickt = 0;
        var cut = Render<Knopf>(ps => ps
            .AddUnmatched("onclick", EventCallback.Factory.Create<MouseEventArgs>(this, () => geklickt++))
            .AddUnmatched("aria-describedby", "hinweis")
            .AddUnmatched("data-test", "eins")
            .AddUnmatched("class", "zl-gross")
            .Add(p => p.Block, true)
            .AddChildContent("Los"));

        var knopf = cut.Find("button");
        knopf.Click();

        Assert.Equal(1, geklickt);
        Assert.Equal("hinweis", knopf.GetAttribute("aria-describedby"));
        Assert.Equal("eins", knopf.GetAttribute("data-test"));
        Assert.Equal("elkw-btn elkw-btn--noarrow elkw-btn--block zl-gross", knopf.GetAttribute("class"));
    }

    // ---------- Card ----------

    [Fact]
    public void Card_hat_Titel_und_Inhalt()
    {
        var cut = Render<Card>(ps => ps.Add(p => p.Titel, "Meine Aufgaben").AddChildContent("<p>Zelt aufbauen</p>"));

        var titel = cut.Find("h2.elkw-card__title");
        Assert.Equal("Meine Aufgaben", titel.TextContent);
        Assert.Equal(titel.Id, cut.Find("section.elkw-card").GetAttribute("aria-labelledby"));
        Assert.Equal("Zelt aufbauen", cut.Find(".elkw-card__body p").TextContent);
    }

    // ---------- Feld ----------

    [Fact]
    public void Feld_hat_sichtbares_Label_ueber_dem_Feld_und_den_Hinweis_darunter()
    {
        var cut = Render<Feld>(ps => ps.Add(p => p.Label, "Vorname").Add(p => p.Id, "vorname").Add(p => p.Name, "vorname")
            .Add(p => p.Wert, "Paul").Add(p => p.Hinweis, "Wie im Ausweis."));

        var kinder = cut.Find(".elkw-field").Children;
        Assert.Equal("LABEL", kinder[0].TagName);
        Assert.Equal("vorname", kinder[0].GetAttribute("for"));
        Assert.Equal("Vorname", kinder[0].TextContent);
        Assert.Equal("INPUT", kinder[1].TagName);
        Assert.Equal("Paul", kinder[1].GetAttribute("value"));
        Assert.Equal("vorname-hinweis", kinder[1].GetAttribute("aria-describedby"));
        Assert.False(kinder[1].HasAttribute("aria-invalid"));
        Assert.Equal("Wie im Ausweis.", cut.Find("#vorname-hinweis").TextContent);
    }

    [Fact]
    public void Feld_mit_Fehler_zeigt_Fehler_Doppelpunkt_und_setzt_aria_invalid_und_aria_describedby()
    {
        var cut = Render<Feld>(ps => ps.Add(p => p.Label, "Geburtsdatum").Add(p => p.Id, "geb").Add(p => p.Typ, "date")
            .Add(p => p.Hinweis, "TT.MM.JJJJ").Add(p => p.Fehler, "Geburtsdatum fehlt."));

        var eingabe = cut.Find("input");
        Assert.Equal("true", eingabe.GetAttribute("aria-invalid"));
        Assert.Equal("geb-hinweis geb-fehler", eingabe.GetAttribute("aria-describedby"));
        Assert.Equal("date", eingabe.GetAttribute("type"));
        Assert.Equal("Fehler: Geburtsdatum fehlt.", cut.Find("#geb-fehler").TextContent);
        Assert.Contains("elkw-field-error", cut.Find("#geb-fehler").ClassList);
    }

    [Fact]
    public void Feld_reicht_weitere_Attribute_an_das_Eingabefeld_weiter_und_kann_deaktiviert_werden()
    {
        var cut = Render<Feld>(ps => ps.Add(p => p.Label, "Ort").Add(p => p.Deaktiviert, true).AddUnmatched("autocomplete", "off"));

        var eingabe = cut.Find("input");
        Assert.Equal("off", eingabe.GetAttribute("autocomplete"));
        Assert.True(eingabe.HasAttribute("disabled"));
        Assert.Equal(eingabe.Id, cut.Find("label").GetAttribute("for"));
    }

    // ---------- Balken ----------

    [Theory]
    [InlineData(BalkenArt.Normal, null, "elkw-status--aktiv")]
    [InlineData(BalkenArt.Warnung, "zl-balken--warnung", "elkw-status--warnung")]
    [InlineData(BalkenArt.Fehler, "zl-balken--fehler", "elkw-status--fehler")]
    public void Balken_hat_Kopfzeile_SVG_ohne_Screenreader_und_Statuswort_mit_Quadrat(BalkenArt art, string? klasse, string statusKlasse)
    {
        var cut = Render<Balken>(ps => ps.Add(p => p.Bezeichnung, "Anmeldungen").Add(p => p.Zahl, "42 von 60")
            .Add(p => p.Prozent, 70).Add(p => p.Statuswort, "Im Plan").Add(p => p.Art, art));

        Assert.Equal("Anmeldungen", cut.Find(".zl-balken__kopf strong").TextContent);
        Assert.Equal("42 von 60", cut.Find(".zl-balken__kopf .zl-zahl").TextContent);
        var svg = cut.Find("svg");
        Assert.Equal("true", svg.GetAttribute("aria-hidden"));
        Assert.Equal("70%", cut.Find("rect").GetAttribute("width"));
        Assert.Null(cut.Find("rect").GetAttribute("style"));
        var status = cut.Find(".elkw-status");
        Assert.Equal("Im Plan", status.TextContent);
        Assert.Contains(statusKlasse, status.ClassList);
        if (klasse is not null)
        {
            Assert.Contains(klasse, cut.Find(".zl-balken").ClassList);
        }
    }

    [Theory]
    [InlineData(150, "100%")]
    [InlineData(-5, "0%")]
    [InlineData(33.333, "33.33%")]
    public void Balken_begrenzt_die_Breite_auf_0_bis_100_Prozent(double prozent, string breite)
    {
        var cut = Render<Balken>(ps => ps.Add(p => p.Bezeichnung, "Fahrt").Add(p => p.Zahl, "1.320 €").Add(p => p.Prozent, prozent)
            .Add(p => p.Statuswort, "Überschritten").Add(p => p.Gross, true));

        Assert.Equal(breite, cut.Find("rect").GetAttribute("width"));
        Assert.Contains("zl-balken--gross", cut.Find(".zl-balken").ClassList);
    }

    // ---------- Status ----------

    [Theory]
    [InlineData(StatusArt.Offen, "elkw-status--offen")]
    [InlineData(StatusArt.Aktiv, "elkw-status--aktiv")]
    [InlineData(StatusArt.Erfolg, "elkw-status--erfolg")]
    [InlineData(StatusArt.Warnung, "elkw-status--warnung")]
    [InlineData(StatusArt.Fehler, "elkw-status--fehler")]
    public void Status_ist_Wort_plus_Quadrat_je_Art(StatusArt art, string klasse)
    {
        var cut = Render<Status>(ps => ps.Add(p => p.Wort, "Bestätigt").Add(p => p.Art, art));

        cut.MarkupMatches($"<span class=\"elkw-status {klasse}\">Bestätigt</span>");
    }

    // ---------- Meldung ----------

    [Theory]
    [InlineData(MeldungArt.Hinweis, "Hinweis", "status", "elkw-alert--hinweis")]
    [InlineData(MeldungArt.Erfolg, "Gespeichert", "status", "elkw-alert--erfolg")]
    [InlineData(MeldungArt.Warnung, "Achtung:", "status", "elkw-alert--warnung")]
    [InlineData(MeldungArt.Fehler, "Fehler", "alert", "elkw-alert--fehler")]
    public void Meldung_hat_Titel_mit_Doppelpunkt_und_role_alert_nur_bei_Fehler(MeldungArt art, string titel, string rolle, string klasse)
    {
        var cut = Render<Meldung>(ps => ps.Add(p => p.Titel, titel).Add(p => p.Art, art).AddChildContent("<p>Text</p>"));

        var meldung = cut.Find("section");
        Assert.Equal(rolle, meldung.GetAttribute("role"));
        Assert.Contains(klasse, meldung.ClassList);
        Assert.Equal(titel.TrimEnd(':') + ":", cut.Find(".elkw-alert__title").TextContent);
        Assert.Equal("Text", cut.Find("p").TextContent);
    }

    // ---------- Inline-Bestätigung ----------

    private IRenderedComponent<InlineBestaetigung> Bestaetigung(Action? ja = null) =>
        Render<InlineBestaetigung>(ps => ps.Add(p => p.AusloeserText, "Alle informieren").Add(p => p.Satz, "Alle informieren?")
            .Add(p => p.JaText, "Ja, informieren").Add(p => p.Bestaetigt, () => ja?.Invoke()));

    [Fact]
    public void Inline_Bestaetigung_klappt_an_Ort_und_Stelle_auf_mit_Satz_Ja_ohne_Pfeil_und_Nein_als_Outline()
    {
        var cut = Bestaetigung();
        Assert.Equal("Alle informieren", cut.Find("button").TextContent);

        cut.Find("button").Click();

        Assert.Equal("Alle informieren?", cut.Find(".zl-bestaetigung p").TextContent);
        var knoepfe = cut.FindAll("button");
        Assert.Equal(2, knoepfe.Count);
        Assert.Equal("Ja, informieren", knoepfe[0].TextContent);
        Assert.Contains("elkw-btn--noarrow", knoepfe[0].ClassList);
        Assert.DoesNotContain("elkw-btn--outline", knoepfe[0].ClassList);
        Assert.Equal("Nein", knoepfe[1].TextContent);
        Assert.Contains("elkw-btn--outline", knoepfe[1].ClassList);
        Assert.Empty(cut.FindAll("dialog"));
    }

    [Fact]
    public void Inline_Bestaetigung_setzt_den_Fokus_auf_Nein()
    {
        var cut = Bestaetigung();

        cut.Find("button").Click();

        var fokus = JSInterop.VerifyFocusAsyncInvoke();
        var nein = cut.FindAll("button")[1];
        Assert.Equal(nein.GetAttribute("blazor:elementReference"), ((ElementReference)fokus.Arguments[0]!).Id);
    }

    [Fact]
    public void Inline_Bestaetigung_Nein_klappt_zu_und_gibt_den_Fokus_an_den_Ausloeser()
    {
        var bestaetigt = false;
        var cut = Bestaetigung(() => bestaetigt = true);
        cut.Find("button").Click();

        cut.FindAll("button")[1].Click();

        var ausloeser = cut.Find("button");
        Assert.Equal("Alle informieren", ausloeser.TextContent);
        Assert.Empty(cut.FindAll(".zl-bestaetigung"));
        Assert.False(bestaetigt);
        var letzterFokus = JSInterop.Invocations.Last(i => i.Identifier == "Blazor._internal.domWrapper.focus");
        Assert.Equal(ausloeser.GetAttribute("blazor:elementReference"), ((ElementReference)letzterFokus.Arguments[0]!).Id);
    }

    [Fact]
    public void Inline_Bestaetigung_Escape_klappt_zu_und_gibt_den_Fokus_an_den_Ausloeser()
    {
        var cut = Bestaetigung();
        cut.Find("button").Click();

        cut.Find(".zl-bestaetigung").KeyDown(Key.Escape);

        var ausloeser = cut.Find("button");
        Assert.Equal("Alle informieren", ausloeser.TextContent);
        var letzterFokus = JSInterop.Invocations.Last(i => i.Identifier == "Blazor._internal.domWrapper.focus");
        Assert.Equal(ausloeser.GetAttribute("blazor:elementReference"), ((ElementReference)letzterFokus.Arguments[0]!).Id);
    }

    [Fact]
    public void Inline_Bestaetigung_Ja_loest_aus_und_klappt_zu()
    {
        var bestaetigt = 0;
        var cut = Bestaetigung(() => bestaetigt++);
        cut.Find("button").Click();

        cut.FindAll("button")[0].Click();

        Assert.Equal(1, bestaetigt);
        Assert.Empty(cut.FindAll(".zl-bestaetigung"));
        var ausloeser = cut.Find("button");
        var letzterFokus = JSInterop.Invocations.Last(i => i.Identifier == "Blazor._internal.domWrapper.focus");
        Assert.Equal(ausloeser.GetAttribute("blazor:elementReference"), ((ElementReference)letzterFokus.Arguments[0]!).Id);
    }

    // ---------- Notfall-Box ----------

    [Fact]
    public void Notfall_Box_hat_roten_Balken_Titel_Satz_und_Notruf_112_ohne_Knopf_ohne_Ziel()
    {
        var cut = Render<NotfallBox>(ps => ps.Add(p => p.Satz, "Offline gilt der ausgedruckte Notfallordner."));

        var box = cut.Find("section");
        Assert.Contains("zl-notfall-box", box.ClassList);
        Assert.Contains("elkw-alert", box.ClassList);
        Assert.Equal("Im Notfall:", cut.Find(".elkw-alert__title").TextContent);
        Assert.Equal(cut.Find(".elkw-alert__title").Id, box.GetAttribute("aria-labelledby"));
        Assert.Contains(cut.FindAll("p"), p => p.TextContent == "Offline gilt der ausgedruckte Notfallordner.");
        var notruf = cut.Find("a[href='tel:112']");
        Assert.Equal("Notruf 112", notruf.TextContent);
        Assert.Contains("zl-gross", notruf.ClassList);
        Assert.DoesNotContain("Notfallhilfe öffnen", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Notfall_Box_mit_Ziel_hat_den_Knopf_Notfallhilfe_oeffnen_mindestens_44_px()
    {
        var cut = Render<NotfallBox>(ps => ps.Add(p => p.Satz, "Notfallplan und Notrufnummern.").Add(p => p.Ziel, "notfall"));

        var knopf = cut.Find("a.elkw-btn");
        Assert.Equal("Notfallhilfe öffnen", knopf.TextContent);
        Assert.Equal("notfall", knopf.GetAttribute("href"));
        Assert.Contains("zl-gross", knopf.ClassList);
        Assert.Contains("elkw-btn--block", knopf.ClassList);
        Assert.DoesNotContain("elkw-btn--noarrow", knopf.ClassList);
        Assert.Single(cut.FindAll("a[href='tel:112']"));
    }

    // ---------- App-Leiste ----------

    [Fact]
    public void App_Leiste_hat_die_Wortmarke_als_Link_und_sonst_nichts()
    {
        var cut = Render<AppLeiste>(ps => ps.Add(p => p.StartHref, "./"));

        var wortmarke = cut.Find("header a.zl-wortmarke");
        Assert.Equal("Zeltlotse", wortmarke.TextContent);
        Assert.Equal("./", wortmarke.GetAttribute("href"));
        Assert.Empty(cut.FindAll(".zl-freizeitname"));
        Assert.Single(cut.FindAll("a"));
        Assert.Empty(cut.FindAll("img"));
    }

    [Fact]
    public void App_Leiste_zeigt_optional_den_Namen_der_Freizeit_und_den_Notfall_Eintrag_mit_rotem_Quadrat()
    {
        var cut = Render<AppLeiste>(ps => ps.Add(p => p.FreizeitName, "Zeltlager 2027").Add(p => p.NotfallHref, "notfall"));

        Assert.Equal("Zeltlager 2027", cut.Find(".zl-freizeitname").TextContent);
        var notfall = cut.Find("a.zl-notfall-eintrag");
        Assert.Equal("notfall", notfall.GetAttribute("href"));
        var status = notfall.QuerySelector(".elkw-status")!;
        Assert.Equal("Notfall", status.TextContent);
        Assert.Contains("elkw-status--fehler", status.ClassList);
    }

    // ---------- App-Rahmen ----------

    [Fact]
    public void App_Rahmen_hat_Leiste_Arbeitsbereich_mit_Kennung_und_optional_Navigationen()
    {
        var ohne = Render<AppRahmen>(ps => ps.Add(p => p.Leiste, "<header>Leiste</header>").AddChildContent("<h1>Start</h1>"));
        var mit = Render<AppRahmen>(ps => ps
            .Add(p => p.Leiste, "<header>Leiste</header>")
            .Add(p => p.UnterDerLeiste, "<div id=\"unter\">Offline</div>")
            .Add(p => p.Navigation, "<nav id=\"seite\">Seitennavigation</nav>")
            .Add(p => p.UntereNavigation, "<nav id=\"unten\">Untere Navigation</nav>")
            .AddChildContent("<h1>Start</h1>"));

        Assert.Contains("zl-app--ohne-nav", ohne.Find(".elkw-app").ClassList);
        Assert.Empty(ohne.FindAll(".elkw-app__nav"));
        Assert.Equal("inhalt", ohne.Find("main.elkw-app__main").Id);
        Assert.Equal("Start", ohne.Find("main h1").TextContent);
        Assert.DoesNotContain("zl-app--ohne-nav", mit.Find(".elkw-app").ClassList);
        Assert.NotNull(mit.Find(".elkw-app__bar #unter"));
        Assert.NotNull(mit.Find(".elkw-app__nav #seite"));
        Assert.NotNull(mit.Find(".zl-app__unten #unten"));
    }

    // ---------- Navigation ----------

    private static readonly NavPunkt[] FuenfPunkte =
    [
        new("Start", "./", Aktiv: true),
        new("Programm", "programm"),
        new("Personen", "personen"),
        new("Kanäle", "kanaele"),
        new("Mehr", "mehr"),
    ];

    [Fact]
    public void Untere_Navigation_hat_fuenf_Punkte_nur_Text_und_aria_current_am_aktiven_Punkt()
    {
        var cut = Render<UntereNavigation>(ps => ps.Add(p => p.Punkte, FuenfPunkte));

        var links = cut.FindAll("nav li > a");
        Assert.Equal(5, links.Count);
        Assert.Equal(["Start", "Programm", "Personen", "Kanäle", "Mehr"], links.Select(l => l.TextContent));
        Assert.Equal("page", links[0].GetAttribute("aria-current"));
        Assert.All(links.Skip(1), l => Assert.False(l.HasAttribute("aria-current")));
        Assert.Empty(cut.FindAll("img, svg, i"));
        Assert.Equal("Hauptbereiche", cut.Find("nav").GetAttribute("aria-label"));
    }

    [Fact]
    public void Untere_Navigation_mit_anderer_Anzahl_als_fuenf_wird_abgelehnt()
    {
        Assert.Throws<ArgumentException>(() => Render<UntereNavigation>(ps => ps.Add(p => p.Punkte, FuenfPunkte[..4])));
    }

    [Fact]
    public void Seitennavigation_hat_Gruppen_mit_Dachzeile_aria_current_Anzahl_als_Text_und_abgesetzten_Punkt()
    {
        NavGruppe[] gruppen =
        [
            new("Planen", [new("Übersicht", "./", Aktiv: true), new("Programm", "programm")]),
            new("Menschen", [new("Anmeldungen", "anmeldungen", Anzahl: 3)]),
        ];

        var cut = Render<Seitennavigation>(ps => ps.Add(p => p.Gruppen, gruppen).Add(p => p.Abgesetzt, new NavPunkt("Einstellungen", "einstellungen")));

        var dachzeilen = cut.FindAll(".elkw-sidenav__group");
        Assert.Equal(["Planen", "Menschen"], dachzeilen.Select(d => d.TextContent));
        Assert.All(dachzeilen, d => Assert.Contains("elkw-kicker", d.ClassList));
        var listen = cut.FindAll("ul[aria-labelledby]");
        Assert.Equal(dachzeilen.Select(d => d.Id), listen.Select(l => l.GetAttribute("aria-labelledby")));
        Assert.Equal("page", cut.Find("a[href='./']").GetAttribute("aria-current"));
        Assert.False(cut.Find("a[href='programm']").HasAttribute("aria-current"));
        Assert.Equal("3", cut.Find("a[href='anmeldungen'] .elkw-count").TextContent);
        Assert.Equal("Einstellungen", cut.Find("ul.zl-sidenav-abgesetzt a").TextContent);
        Assert.Empty(cut.FindAll("img, svg"));
    }

    // ---------- Offline ----------

    [Fact]
    public void Offline_Leiste_zeigt_den_Text_als_Parameter()
    {
        var cut = Render<OfflineLeiste>(ps => ps.Add(p => p.Text, "Offline – zuletzt aktualisiert 14:32 Uhr"));

        Assert.Equal("Offline – zuletzt aktualisiert 14:32 Uhr", cut.Find(".zl-offline-leiste p").TextContent);
        Assert.Equal("status", cut.Find(".zl-offline-leiste").GetAttribute("role"));
    }

    [Fact]
    public void Offline_Bereich_ist_offline_inert_ausgegraut_und_mit_dem_Satz()
    {
        var cut = Render<OfflineBereich>(ps => ps.Add(p => p.Offline, true).AddChildContent("<button>Bearbeiten</button>"));

        var bereich = cut.Find(".zl-offline-bereich");
        Assert.True(bereich.HasAttribute("inert"));
        Assert.Contains("zl-offline-bereich--aus", bereich.ClassList);
        Assert.Equal("Offline nicht verfügbar.", cut.Find("p.zl-offline-satz").TextContent);
        Assert.NotNull(bereich.QuerySelector("button"));
    }

    [Fact]
    public void Offline_Bereich_ist_online_unveraendert()
    {
        var cut = Render<OfflineBereich>(ps => ps.Add(p => p.Offline, false).AddChildContent("<button>Bearbeiten</button>"));

        var bereich = cut.Find(".zl-offline-bereich");
        Assert.False(bereich.HasAttribute("inert"));
        Assert.DoesNotContain("zl-offline-bereich--aus", bereich.ClassList);
        Assert.DoesNotContain("Offline nicht verfügbar.", cut.Markup, StringComparison.Ordinal);
    }

    // ---------- Leer-Zustand ----------

    [Fact]
    public void Leer_Zustand_ist_ein_Satz_plus_Standard_Knopf()
    {
        var cut = Render<LeerZustand>(ps => ps.Add(p => p.Satz, "Noch keine Programmpunkte.")
            .Add(p => p.KnopfText, "Ersten Programmpunkt anlegen").Add(p => p.KnopfHref, "programm/neu"));

        Assert.Equal("Noch keine Programmpunkte.", cut.Find(".elkw-empty p").TextContent);
        var knopf = cut.Find(".elkw-empty a.elkw-btn");
        Assert.Equal("Ersten Programmpunkt anlegen", knopf.TextContent);
        Assert.DoesNotContain("elkw-btn--outline", knopf.ClassList);
        Assert.DoesNotContain("elkw-btn--text", knopf.ClassList);
    }

    [Fact]
    public void Leer_Zustand_nimmt_einen_eigenen_Knopf_als_Inhalt()
    {
        var cut = Render<LeerZustand>(ps => ps.Add(p => p.Satz, "Noch keine Aufgaben.").AddChildContent("<button>Vorlage übernehmen</button>"));

        Assert.Equal("Vorlage übernehmen", cut.Find(".elkw-empty button").TextContent);
    }

    // ---------- Speicher-Hinweis ----------

    [Fact]
    public void Speicher_Hinweis_gespeichert_mit_Rueckgaengig_als_Textbutton_und_role_status()
    {
        var rueckgaengig = 0;
        var cut = Render<SpeicherHinweis>(ps => ps.Add(p => p.Zustand, SpeicherZustand.Gespeichert).Add(p => p.MitRueckgaengig, true)
            .Add(p => p.Rueckgaengig, () => rueckgaengig++));

        var hinweis = cut.Find("p");
        Assert.Equal("status", hinweis.GetAttribute("role"));
        Assert.StartsWith("Gespeichert · ", hinweis.TextContent, StringComparison.Ordinal);
        var knopf = cut.Find("button");
        Assert.Equal("Rückgängig", knopf.TextContent);
        Assert.Contains("elkw-btn--text", knopf.ClassList);
        knopf.Click();
        Assert.Equal(1, rueckgaengig);
    }

    [Fact]
    public void Speicher_Hinweis_gespeichert_ohne_Rueckgaengig()
    {
        var cut = Render<SpeicherHinweis>(ps => ps.Add(p => p.Zustand, SpeicherZustand.Gespeichert));

        Assert.Equal("Gespeichert", cut.Find("p").TextContent);
        Assert.Empty(cut.FindAll("button"));
    }

    [Fact]
    public void Speicher_Hinweis_Konflikt_mit_Textbutton_Neu_laden()
    {
        var neuLaden = 0;
        var cut = Render<SpeicherHinweis>(ps => ps.Add(p => p.Zustand, SpeicherZustand.Konflikt).Add(p => p.NeuLaden, () => neuLaden++));

        Assert.Equal("status", cut.Find("p").GetAttribute("role"));
        Assert.StartsWith("Inzwischen geändert – neu laden?", cut.Find("p").TextContent, StringComparison.Ordinal);
        var knopf = cut.Find("button");
        Assert.Equal("Neu laden", knopf.TextContent);
        Assert.Contains("elkw-btn--text", knopf.ClassList);
        knopf.Click();
        Assert.Equal(1, neuLaden);
    }

    [Fact]
    public void Speicher_Hinweis_Gescheitert_mit_Knopf_Erneut_versuchen_und_role_alert()
    {
        var erneut = 0;
        var cut = Render<SpeicherHinweis>(ps => ps.Add(p => p.Zustand, SpeicherZustand.Gescheitert).Add(p => p.ErneutVersuchen, () => erneut++));

        Assert.Equal("alert", cut.Find("p").GetAttribute("role"));
        Assert.StartsWith("Nicht gespeichert.", cut.Find("p").TextContent, StringComparison.Ordinal);
        var knopf = cut.Find("button");
        Assert.Equal("Erneut versuchen", knopf.TextContent);
        Assert.DoesNotContain("elkw-btn--text", knopf.ClassList);
        knopf.Click();
        Assert.Equal(1, erneut);
    }

    [Theory]
    [InlineData(SpeicherZustand.Gespeichert)]
    [InlineData(SpeicherZustand.Konflikt)]
    [InlineData(SpeicherZustand.Gescheitert)]
    public void Speicher_Hinweis_deaktiviert_die_Knoepfe_waehrend_der_Uebertragung(SpeicherZustand zustand)
    {
        var cut = Render<SpeicherHinweis>(ps => ps.Add(p => p.Zustand, zustand).Add(p => p.MitRueckgaengig, true).Add(p => p.Uebertraegt, true));

        Assert.True(cut.Find("button").HasAttribute("disabled"));
    }

    [Fact]
    public void Speicher_Hinweis_ohne_Zustand_zeigt_nichts()
    {
        var cut = Render<SpeicherHinweis>(ps => ps.Add(p => p.Zustand, SpeicherZustand.Keiner));

        Assert.Equal("", cut.Markup.Trim());
    }

    // ---------- Sprunglink, Ladezustand, öffentlicher Rahmen ----------

    [Fact]
    public void Sprunglink_zeigt_Zum_Inhalt_springen_auf_den_Arbeitsbereich()
    {
        var cut = Render<Sprunglink>();

        cut.MarkupMatches("<a class=\"elkw-skiplink\" href=\"#inhalt\">Zum Inhalt springen</a>");
    }

    [Fact]
    public void Ladezustand_zeigt_Wird_geladen_mit_aria_busy_ohne_Spinner()
    {
        var cut = Render<Ladezustand>();

        var text = cut.Find("p");
        Assert.Equal("Wird geladen …", text.TextContent);
        Assert.Equal("true", text.GetAttribute("aria-busy"));
        Assert.Empty(cut.FindAll("svg, img"));
    }

    [Fact]
    public void Oeffentlicher_Rahmen_hat_Sprunglink_Name_der_Organisation_und_eine_Spalte()
    {
        var vorgabe = Render<OeffentlicherRahmen>(ps => ps.AddChildContent("<h1>Zeltlager</h1>"));
        var eigene = Render<OeffentlicherRahmen>(ps => ps.Add(p => p.Organisation, "EJW Dornstetten"));

        Assert.Equal("Zum Inhalt springen", vorgabe.Find("a.elkw-skiplink").TextContent);
        Assert.Equal("Zeltlotse", vorgabe.Find("header .zl-wortmarke").TextContent);
        Assert.Equal("inhalt", vorgabe.Find("main").Id);
        Assert.Contains("zl-oeffentlich-spalte", vorgabe.Find("main").ClassList);
        Assert.Equal("EJW Dornstetten", eigene.Find("header .zl-wortmarke").TextContent);
        Assert.Empty(vorgabe.FindAll("nav"));
    }

    [Fact]
    public void Stile_binden_die_Stylesheets_in_der_vorgeschriebenen_Reihenfolge_ein()
    {
        var cut = Render<Stile>();

        Assert.Equal(
            [
                "/_content/Zeltlotse.Ui.Komponenten/elkw-fonts.css",
                "/_content/Zeltlotse.Ui.Komponenten/css/elkw-tokens.css",
                "/_content/Zeltlotse.Ui.Komponenten/css/elkw-components.css",
                "/_content/Zeltlotse.Ui.Komponenten/css/elkw-app.css",
                "/_content/Zeltlotse.Ui.Komponenten/css/elkw-nahbar.css",
                "/_content/Zeltlotse.Ui.Komponenten/css/zeltlotse.css",
            ],
            cut.FindAll("link[rel=stylesheet]").Select(l => l.GetAttribute("href")));
    }
}
