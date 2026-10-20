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
        Assert.False(cut.Find("section.elkw-card").HasAttribute("id"));
    }

    [Fact]
    public void Card_mit_Kennung_ist_Sprungziel()
    {
        var cut = Render<Card>(ps => ps.Add(p => p.Titel, "Freizeitpässe unvollständig").Add(p => p.Id, "freizeitpaesse-offen"));

        Assert.Equal("freizeitpaesse-offen", cut.Find("section.elkw-card").Id);
    }

    [Fact]
    public void Card_mit_Dachzeile_und_Titel_Link()
    {
        // Story 3-2: je Programmpunkt eine Card in der Materialliste, Tag und Uhrzeit als Dachzeile, der Titel führt zur Detailseite.
        var cut = Render<Card>(ps => ps.Add(p => p.Titel, "Geländespiel").Add(p => p.Dachzeile, "Di 03.08. · 14:30").Add(p => p.TitelHref, "programm/1"));

        var body = cut.Find(".elkw-card__body");
        Assert.Equal("Di 03.08. · 14:30", body.QuerySelector("p.elkw-kicker:first-child")!.TextContent);
        var link = cut.Find("h2.elkw-card__title a");
        Assert.Equal(("Geländespiel", "programm/1"), (link.TextContent, link.GetAttribute("href")));
        Assert.Equal(cut.Find("h2").Id, cut.Find("section.elkw-card").GetAttribute("aria-labelledby"));
        Assert.Empty(Render<Card>(ps => ps.Add(p => p.Titel, "Ohne")).FindAll(".elkw-kicker, a"));
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

    [Fact]
    public void Feld_mehrzeilig_ist_ein_textarea_mit_Label_Hinweis_und_Wert()
    {
        var cut = Render<Feld>(ps => ps.Add(p => p.Label, "Beschreibung").Add(p => p.Id, "beschreibung").Add(p => p.Mehrzeilig, true)
            .Add(p => p.Wert, "Zeile 1\nZeile 2").Add(p => p.Hinweis, "Höchstens 2000 Zeichen").AddUnmatched("maxlength", "2000"));

        var kinder = cut.Find(".elkw-field").Children;
        Assert.Equal("LABEL", kinder[0].TagName);
        Assert.Equal("beschreibung", kinder[0].GetAttribute("for"));
        Assert.Equal("TEXTAREA", kinder[1].TagName);
        Assert.Equal("beschreibung", kinder[1].Id);
        Assert.Equal("Zeile 1\nZeile 2", kinder[1].GetAttribute("value") ?? kinder[1].TextContent);
        Assert.Equal("2000", kinder[1].GetAttribute("maxlength"));
        Assert.Equal("beschreibung-hinweis", kinder[1].GetAttribute("aria-describedby"));
        Assert.Empty(cut.FindAll("input"));
    }

    // ---------- Dachzeile ----------

    [Fact]
    public void Dachzeile_ist_ein_Absatz_mit_der_Kicker_Klasse()
    {
        var cut = Render<Dachzeile>(ps => ps.AddChildContent("Freizeit · 2. bis 13. August 2027"));

        cut.MarkupMatches("""<p class="elkw-kicker">Freizeit · 2. bis 13. August 2027</p>""");
    }

    // ---------- Personenliste ----------

    [Fact]
    public void Personenliste_zeigt_Avatar_mit_Initialen_ohne_Vorlesen_Name_und_Zeile()
    {
        var cut = Render<PersonenListe>(ps => ps.Add(p => p.Personen, new[]
        {
            new PersonenEintrag("MZ", "Miriam", "Leitung · aus Musterstadt"),
            new PersonenEintrag("J", "Jonas", null),
        }));

        var liste = cut.Find("ul.elkw-people");
        var eintraege = liste.Children;
        Assert.Equal(2, eintraege.Length);
        Assert.Equal(
            """<span class="elkw-avatar" aria-hidden="true">MZ</span>""",
            eintraege[0].QuerySelector(".elkw-avatar")!.OuterHtml);
        Assert.Equal("Miriam", eintraege[0].QuerySelector(".elkw-people__who strong")!.TextContent);
        Assert.Equal("Leitung · aus Musterstadt", eintraege[0].QuerySelector(".elkw-people__who span")!.TextContent);
        Assert.Equal("J", eintraege[1].QuerySelector(".elkw-avatar")!.TextContent);
        Assert.Null(eintraege[1].QuerySelector(".elkw-people__who span"));
        Assert.Empty(cut.FindAll("button, a, img"));
    }

    [Fact]
    public void Personenliste_mit_Href_zeigt_den_Namen_als_Link()
    {
        var cut = Render<PersonenListe>(ps => ps.Add(p => p.Personen, new[]
        {
            new PersonenEintrag("EM", "Emma Muster", "Teilnehmende", Href: "freizeiten/x/freizeitpaesse/e"),
            new PersonenEintrag("LF", "Lea Feuerstein", "Mitarbeitende"),
        }));

        var link = cut.Find(".elkw-people__who strong a");
        Assert.Equal(("Emma Muster", "freizeiten/x/freizeitpaesse/e"), (link.TextContent, link.GetAttribute("href")));
        Assert.Single(cut.FindAll("a"));
        Assert.Equal("Lea Feuerstein", cut.FindAll(".elkw-people__who strong")[1].TextContent);
        Assert.Equal("Teilnehmende", cut.Find(".elkw-people__who span").TextContent);
    }

    // ---------- Telefon-Link ----------

    [Fact]
    public void Telefon_Link_waehlt_nur_Plus_und_Ziffern_und_ist_mindestens_44_px_hoch()
    {
        var cut = Render<TelefonLink>(ps => ps.Add(p => p.Nummer, "+49 (0123) 456-782").Add(p => p.Text, "0123 456782 anrufen"));

        cut.MarkupMatches("""<a class="elkw-link zl-gross" href="tel:+490123456782">0123 456782 anrufen</a>""");
        Assert.Equal("tel:112", TelefonLink.Href("112"));
        Assert.Equal("tel:0123456783", TelefonLink.Href(" 0123 / 45 67 83 "));
    }

    [Fact]
    public void Telefon_Link_laesst_mit_Laendervorwahl_die_Null_in_Klammern_weg()
    {
        Assert.Equal("tel:+4974431234", TelefonLink.Href("+49 (0) 7443 1234"));
        Assert.Equal("tel:+4974431234", TelefonLink.Href(" +49 ( 0 )7443-1234"));
        // Ohne „+“ bleibt die Null: Sie gehört zur Vorwahl.
        Assert.Equal("tel:074431234", TelefonLink.Href("(0)7443 1234"));
    }

    // ---------- Druck (Story 2-11) ----------

    [Fact]
    public void Druck_Seite_ist_ein_Abschnitt_mit_Dachzeile_und_Titel_als_h2()
    {
        var cut = Render<DruckSeite>(ps => ps.Add(p => p.Dachzeile, "Zeltlager Sommer 2027").Add(p => p.Titel, "Mia Probst").AddChildContent("<p>Inhalt</p>"));

        cut.MarkupMatches("""
            <section class="zl-druck-seite">
              <p class="elkw-kicker">Zeltlager Sommer 2027</p>
              <h2 class="zl-druck-seite__titel">Mia Probst</h2>
              <p>Inhalt</p>
            </section>
            """);
    }

    [Fact]
    public void Infokasten_ist_umrandet_mit_Titel_als_h3_und_traegt_die_Information_im_Text()
    {
        var cut = Render<Infokasten>(ps => ps.Add(p => p.Titel, "Notfallkontakte").AddChildContent("<p>Daniel Probst (Vater), 0123 456781</p>"));

        cut.MarkupMatches("""
            <div class="zl-infokasten">
              <h3 class="zl-infokasten__titel">Notfallkontakte</h3>
              <p>Daniel Probst (Vater), 0123 456781</p>
            </div>
            """);
    }

    [Fact]
    public void Nur_Bildschirm_umschliesst_den_Inhalt_der_beim_Drucken_wegfaellt()
    {
        var cut = Render<NurBildschirm>(ps => ps.AddChildContent("<h1>Notfallordner</h1>"));

        cut.MarkupMatches("""<div class="zl-nur-bildschirm"><h1>Notfallordner</h1></div>""");
    }

    [Fact]
    public void Druck_CSS_setzt_A4_blendet_Bildschirm_Teile_aus_und_bricht_ab_der_zweiten_Druckseite_um()
    {
        var css = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Zeltlotse.Ui.Komponenten", "wwwroot", "css", "zeltlotse.css"));
        var druck = css[css.IndexOf("/* ---------- 5. Druck", StringComparison.Ordinal)..];

        Assert.Contains("@page { size: A4; margin: 15mm; }", druck, StringComparison.Ordinal);
        Assert.Contains(".zl-nur-bildschirm", druck, StringComparison.Ordinal);
        Assert.Contains(".zl-druck-seite ~ .zl-druck-seite { break-before: page; }", druck, StringComparison.Ordinal);
        Assert.Contains("break-inside: avoid", druck, StringComparison.Ordinal);
        Assert.Contains("font-size: 10.5pt", druck, StringComparison.Ordinal);
        Assert.Contains("font-size: 26pt", druck, StringComparison.Ordinal);
        Assert.Contains("font-size: 15pt", druck, StringComparison.Ordinal);
    }

    // ---------- Statusliste ----------

    [Fact]
    public void Statusliste_zeigt_je_Zeile_Text_und_Status_Wort_mit_Quadrat()
    {
        var cut = Render<StatusListe>(ps => ps.Add(p => p.Eintraege, new[]
        {
            new StatusEintrag("Zeltlager Sommer 2027, 02.08.2027 – 13.08.2027", "Eingegangen", StatusArt.Offen),
            new StatusEintrag("Herbstfreizeit", "Bestätigt", StatusArt.Erfolg),
        }));

        var zeilen = cut.FindAll("ul.zl-statusliste li");
        Assert.Equal(2, zeilen.Count);
        Assert.Equal("Zeltlager Sommer 2027, 02.08.2027 – 13.08.2027", zeilen[0].QuerySelector("span")!.TextContent);
        Assert.Equal("<span class=\"elkw-status elkw-status--offen\">Eingegangen</span>", zeilen[0].QuerySelector(".elkw-status")!.OuterHtml);
        Assert.Equal("<span class=\"elkw-status elkw-status--erfolg\">Bestätigt</span>", zeilen[1].QuerySelector(".elkw-status")!.OuterHtml);
        Assert.Empty(cut.FindAll("button, a"));
    }

    [Fact]
    public void Statusliste_ohne_Eintraege_zeigt_nichts()
    {
        var cut = Render<StatusListe>(ps => ps.Add(p => p.Eintraege, Array.Empty<StatusEintrag>()));

        Assert.Empty(cut.FindAll("ul"));
    }

    // ---------- Details-Liste (Story 2-5) ----------

    [Fact]
    public void Details_Liste_zeigt_Begriff_und_Wert_nur_lesend()
    {
        var cut = Render<DetailsListe>(ps => ps.Add(p => p.Eintraege, new[]
        {
            new DetailsEintrag("Allergien und Unverträglichkeiten", "Glutenhaltiges Getreide"),
            new DetailsEintrag("Kann schwimmen", "Nein"),
        }));

        Assert.Equal(["Allergien und Unverträglichkeiten", "Kann schwimmen"], cut.FindAll("dl.elkw-details dt").Select(e => e.TextContent));
        Assert.Equal(["Glutenhaltiges Getreide", "Nein"], cut.FindAll("dl.elkw-details dd").Select(e => e.TextContent));
        Assert.Empty(cut.FindAll("button, a, input"));
    }

    [Fact]
    public void Details_Liste_ohne_Eintraege_zeigt_nichts()
    {
        var cut = Render<DetailsListe>(ps => ps.Add(p => p.Eintraege, Array.Empty<DetailsEintrag>()));

        Assert.Empty(cut.FindAll("dl"));
    }

    // ---------- Fehlerübersicht ----------

    [Fact]
    public void Fehleruebersicht_ist_eine_Fehler_Meldung_mit_Sprunglinks_zu_den_Feldern()
    {
        var cut = Render<Fehleruebersicht>(ps => ps.Add(p => p.Fehler, new[]
        {
            new FeldFehler("kind-vorname", "Kind: Vorname fehlt."),
            new FeldFehler("eltern-mail", "Elternteil: Mail-Adresse fehlt."),
        }));

        var uebersicht = cut.Find("section");
        Assert.Equal("alert", uebersicht.GetAttribute("role"));
        Assert.Contains("elkw-alert--fehler", uebersicht.ClassList);
        var titel = cut.Find(".elkw-alert__title");
        Assert.Equal(titel.Id, uebersicht.GetAttribute("aria-labelledby"));
        Assert.StartsWith("Fehler:", titel.TextContent, StringComparison.Ordinal);
        Assert.Equal(
            ["#kind-vorname Kind: Vorname fehlt.", "#eltern-mail Elternteil: Mail-Adresse fehlt."],
            cut.FindAll("li a").Select(a => $"{a.GetAttribute("href")} {a.TextContent}"));
        // Statisch tauglich: nur Links, keine Knöpfe.
        Assert.Empty(cut.FindAll("button"));
    }

    [Fact]
    public void Fehleruebersicht_ohne_Fehler_zeigt_nichts()
    {
        var cut = Render<Fehleruebersicht>(ps => ps.Add(p => p.Fehler, Array.Empty<FeldFehler>()));

        Assert.Empty(cut.Markup.Trim());
    }

    [Fact]
    public void Personenliste_ohne_Personen_zeigt_nichts()
    {
        var cut = Render<PersonenListe>(ps => ps.Add(p => p.Personen, Array.Empty<PersonenEintrag>()));

        Assert.Empty(cut.FindAll("ul"));
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

    // ---------- Story 3-1: Reiter, Termin, Tagesspalten ----------

    [Fact]
    public void Reiter_sind_Links_der_aktive_traegt_aria_current_und_der_Inhalt_steht_darunter()
    {
        ReiterEintrag[] eintraege = [new("Mo 02.08.", "programm?tag=2027-08-02"), new("Di 03.08.", "programm?tag=2027-08-03", Aktiv: true)];

        var cut = Render<Reiter>(ps => ps.Add(p => p.Eintraege, eintraege).Add(p => p.Bezeichnung, "Tage").AddChildContent("<h2>Dienstag</h2>"));

        Assert.Equal("Tage", cut.Find("nav").GetAttribute("aria-label"));
        var links = cut.FindAll("ul.elkw-tabs li a");
        Assert.Equal(["Mo 02.08.", "Di 03.08."], links.Select(l => l.TextContent));
        Assert.Equal(["programm?tag=2027-08-02", "programm?tag=2027-08-03"], links.Select(l => l.GetAttribute("href")));
        Assert.False(links[0].HasAttribute("aria-current"));
        Assert.Equal("page", links[1].GetAttribute("aria-current"));
        Assert.Equal("Dienstag", cut.Find(".zl-reiter__inhalt h2").TextContent);
        Assert.Empty(cut.FindAll("button"));
        Assert.DoesNotContain("zl-nur-handy", cut.Find(".zl-reiter").ClassList);
    }

    [Fact]
    public void Reiter_nur_im_Handy_Aufbau_tragen_die_Klasse_und_ohne_Inhalt_keinen_Inhaltsbereich()
    {
        var cut = Render<Reiter>(ps => ps.Add(p => p.Eintraege, [new ReiterEintrag("Mo 02.08.", "x", true)]).Add(p => p.Bezeichnung, "Tage").Add(p => p.NurHandy, true));

        Assert.Contains("zl-nur-handy", cut.Find(".zl-reiter").ClassList);
        Assert.Empty(cut.FindAll(".zl-reiter__inhalt"));
    }

    // ---------- Story 4-2: Vergleich ----------

    [Fact]
    public void Vergleich_zeigt_Vorher_und_Nachher_als_zwei_Spalten_geaenderte_Zeilen_fett_und_vorgelesen()
    {
        VergleichZeile[] vorher = [new("Mehl", "1 g", Geaendert: true), new("Salz", null, Geaendert: false)];
        VergleichZeile[] nachher = [new("Kidneybohnen", "250 g", Geaendert: true)];

        var cut = Render<Vergleich>(ps => ps
            .Add(p => p.Bezeichnung, "Tausch Pfannkuchen gegen Chili sin Carne")
            .Add(p => p.VorherText, "Pfannkuchen")
            .Add(p => p.Vorher, vorher)
            .Add(p => p.NachherText, "Chili sin Carne")
            .Add(p => p.Nachher, nachher));

        Assert.Equal("Tausch Pfannkuchen gegen Chili sin Carne", cut.Find(".zl-vergleich").GetAttribute("aria-label"));
        var spalten = cut.FindAll(".zl-vergleich__spalte");
        Assert.Equal(["Vorher", "Nachher"], spalten.Select(s => s.QuerySelector("h3")!.TextContent));
        Assert.Equal(["Pfannkuchen", "Chili sin Carne"], spalten.Select(s => s.QuerySelector("p")!.TextContent));
        var zeilen = spalten[0].QuerySelectorAll("li");
        Assert.Equal("1 g Mehl", zeilen[0].QuerySelector("strong")!.TextContent);
        Assert.Contains("(geändert)", zeilen[0].QuerySelector(".zl-nur-vorlesen")!.TextContent, StringComparison.Ordinal);
        Assert.Null(zeilen[1].QuerySelector("strong"));
        Assert.Equal("Salz", zeilen[1].TextContent.Trim());
        Assert.Equal("250 g Kidneybohnen", spalten[1].QuerySelector("li strong")!.TextContent);
        Assert.Empty(cut.FindAll("button"));
    }

    [Fact]
    public void Vergleich_ohne_Zeilen_nennt_das()
    {
        var cut = Render<Vergleich>(ps => ps.Add(p => p.Bezeichnung, "Tausch").Add(p => p.Vorher, []).Add(p => p.Nachher, [new VergleichZeile("Reis", "1 kg", false)]));

        Assert.Equal("Keine Posten.", cut.FindAll(".zl-vergleich__spalte")[0].QuerySelector("p")!.TextContent);
        Assert.Equal(["Vorher", "Nachher"], cut.FindAll("h3").Select(h => h.TextContent));
    }

    [Fact]
    public void Termin_zeigt_Uhrzeit_als_Dachzeile_Titel_als_Link_Status_und_Inhalt()
    {
        var cut = Render<Termin>(ps => ps
            .Add(p => p.Uhrzeit, "20:00")
            .Add(p => p.Titel, "Lagerfeuer")
            .Add(p => p.Href, "programm/1")
            .Add(p => p.StatusWort, "Übernommen: Lea Feuerstein")
            .Add(p => p.StatusArt, StatusArt.Erfolg)
            .AddChildContent("<button>Abgeben</button>"));

        var termin = cut.Find("article.zl-termin");
        Assert.Equal("20:00", termin.QuerySelector(".elkw-kicker")!.TextContent);
        var titel = termin.QuerySelector("h3")!;
        Assert.Equal(titel.Id, termin.GetAttribute("aria-labelledby"));
        Assert.Equal(("Lagerfeuer", "programm/1"), (titel.QuerySelector("a")!.TextContent, titel.QuerySelector("a")!.GetAttribute("href")));
        var status = termin.QuerySelector(".elkw-status")!;
        Assert.Equal("Übernommen: Lea Feuerstein", status.TextContent);
        Assert.Contains("elkw-status--erfolg", status.ClassList);
        Assert.Equal("Abgeben", termin.QuerySelector(".zl-termin__inhalt button")!.TextContent);
    }

    [Fact]
    public void Termin_ohne_Uhrzeit_Link_und_Status_ist_nur_ein_Titel()
    {
        var cut = Render<Termin>(ps => ps.Add(p => p.Titel, "Abbau"));

        Assert.Empty(cut.FindAll(".elkw-kicker"));
        Assert.Empty(cut.FindAll("a"));
        Assert.Empty(cut.FindAll(".elkw-status"));
        Assert.Empty(cut.FindAll(".zl-termin__inhalt"));
        Assert.Equal("Abbau", cut.Find("h3").TextContent.Trim());
    }

    [Fact]
    public void TagesSpalten_haben_je_Tag_eine_Spalte_mit_Tageskopf_und_sind_als_Bereich_per_Tastatur_erreichbar()
    {
        TagesSpalte[] spalten =
        [
            new("Mo 02.08.", b => b.AddMarkupContent(0, "<p>Frühstück</p>")),
            new("Di 03.08.", b => b.AddMarkupContent(0, "<p>Geländespiel</p>")),
        ];

        var cut = Render<TagesSpalten>(ps => ps.Add(p => p.Spalten, spalten).Add(p => p.Bezeichnung, "Wochenraster"));

        var bereich = cut.Find(".zl-tagesspalten");
        Assert.Equal(("region", "Wochenraster", "0"), (bereich.GetAttribute("role"), bereich.GetAttribute("aria-label"), bereich.GetAttribute("tabindex")));
        var abschnitte = cut.FindAll("section.zl-tagesspalte");
        Assert.Equal(["Mo 02.08.", "Di 03.08."], abschnitte.Select(a => a.QuerySelector("h2")!.TextContent));
        Assert.All(abschnitte, a => Assert.Equal(a.QuerySelector("h2")!.Id, a.GetAttribute("aria-labelledby")));
        Assert.Equal(["Frühstück", "Geländespiel"], abschnitte.Select(a => a.QuerySelector("p")!.TextContent));
    }

    // ---------- Story 3-2: Segmente ----------

    private static readonly AuswahlOption[] Zustaende =
        [new("Gebraucht", "gebraucht"), new("Vorhanden", "vorhanden"), new("Besorgt", "besorgt"), new("Eingepackt", "eingepackt")];

    [Fact]
    public void Segmente_sind_eine_Gruppe_mit_Legende_je_Option_ein_Knopf_und_aria_pressed_am_gewaehlten()
    {
        var cut = Render<Segmente>(ps => ps.Add(p => p.Legende, "Status von Seile").Add(p => p.Optionen, Zustaende).Add(p => p.Wert, "Besorgt"));

        var gruppe = cut.Find("div.elkw-segments");
        Assert.Equal(("group", "Status von Seile"), (gruppe.GetAttribute("role"), gruppe.GetAttribute("aria-label")));
        var knoepfe = gruppe.QuerySelectorAll("button");
        Assert.Equal(["gebraucht", "vorhanden", "besorgt", "eingepackt"], knoepfe.Select(k => k.TextContent));
        Assert.All(knoepfe, k => Assert.Equal("button", k.GetAttribute("type")));
        Assert.Equal(["false", "false", "true", "false"], knoepfe.Select(k => k.GetAttribute("aria-pressed")));
        Assert.All(knoepfe, k => Assert.False(k.HasAttribute("disabled")));
    }

    [Fact]
    public void Segmente_ein_Tipp_meldet_den_Wert_der_gewaehlte_Wert_loest_nichts_aus()
    {
        var gewaehlt = new List<string>();
        var cut = Render<Segmente>(ps => ps.Add(p => p.Legende, "Status").Add(p => p.Optionen, Zustaende).Add(p => p.Wert, "Gebraucht")
            .Add(p => p.Gewaehlt, (string wert) => gewaehlt.Add(wert)));

        cut.FindAll("button")[3].Click();
        cut.FindAll("button")[0].Click();

        Assert.Equal(["Eingepackt"], gewaehlt);
    }

    [Fact]
    public void Segmente_deaktiviert_setzen_disabled_an_jedem_Knopf()
    {
        var cut = Render<Segmente>(ps => ps.Add(p => p.Legende, "Status").Add(p => p.Optionen, Zustaende).Add(p => p.Deaktiviert, true));

        Assert.All(cut.FindAll("button"), k => Assert.True(k.HasAttribute("disabled")));
        Assert.All(cut.FindAll("button"), k => Assert.Equal("false", k.GetAttribute("aria-pressed")));
    }

    // ---------- Story 5-1: Schieberegler ----------

    [Fact]
    public void Schieberegler_ist_ein_range_mit_Label_Grenzen_Schritt_und_dem_Wert_als_Zahl_daneben()
    {
        var cut = Render<Schieberegler>(ps => ps.Add(p => p.Label, "Tagessatz Verpflegung").Add(p => p.Id, "regler-tagessatz").Add(p => p.Minimum, 0m)
            .Add(p => p.Maximum, 30m).Add(p => p.Schritt, 0.5m).Add(p => p.Wert, 7.5m).Add(p => p.Anzeige, (Func<decimal, string>)(w => $"{w:0.00} €"))
            .Add(p => p.Hinweis, "In Schritten von 0,50 €"));

        var regler = cut.Find("input");
        Assert.Equal(("range", "0", "30", "0.5", "7.5"), (regler.GetAttribute("type"), regler.GetAttribute("min"), regler.GetAttribute("max"), regler.GetAttribute("step"), regler.GetAttribute("value")));
        Assert.Equal("Tagessatz Verpflegung", cut.Find("label[for=regler-tagessatz]").TextContent);
        var ausgabe = cut.Find("output");
        Assert.Equal(("regler-tagessatz", $"{7.5m:0.00} €"), (ausgabe.GetAttribute("for"), ausgabe.TextContent));
        Assert.Equal($"{7.5m:0.00} €", regler.GetAttribute("aria-valuetext"));
        Assert.Equal("regler-tagessatz-hinweis", regler.GetAttribute("aria-describedby"));
        Assert.Null(regler.GetAttribute("style"));
    }

    [Fact]
    public void Schieberegler_meldet_jede_Bewegung_sofort_auf_den_Schritt_gerundet_und_begrenzt()
    {
        var werte = new List<decimal>();
        var cut = Render<Schieberegler>(ps => ps.Add(p => p.Label, "Teilnehmende").Add(p => p.Maximum, 200m).Add(p => p.Wert, 40m)
            .Add(p => p.WertGeaendert, (decimal w) => werte.Add(w)));

        Assert.Equal("40", cut.Find("output").TextContent);
        cut.Find("input").Input("41");
        cut.Find("input").Input("12.3");
        cut.Find("input").Input("250");
        cut.Find("input").Input("abc");

        Assert.Equal([41m, 12m, 200m], werte);
        Assert.Equal("200", cut.Find("output").TextContent);
    }
}
