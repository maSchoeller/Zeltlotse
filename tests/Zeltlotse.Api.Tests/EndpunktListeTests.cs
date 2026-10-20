using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.SharedKernel.Rechte;
using Zeltlotse.SharedKernel.Web;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Autorisierung aller <c>/api</c>-Endpunkte (AD-12, AD-26): Jeder verlangt eine Sitzung, außer der ausdrücklich anonymen
/// Liste; jeder unter <c>/api/ich/</c> trägt <c>.Organisationsuebergreifend()</c> und steht namentlich in der Liste.
/// Neue Endpunkte müssen hier bewusst eingetragen werden.
/// </summary>
public sealed class EndpunktListeTests(PostgresContainer postgres)
{
    /// <summary>
    /// Die anonymen Endpunkte: Betrieb, die vier Anmelde-Endpunkte, die zwei Anmeldelink-Seiten (Story 2-1), die Gast-Anmeldung mit
    /// Gast-Link (Story 2-2) und der Freizeitpass über den Gast-Link (Story 2-4).
    /// </summary>
    private static readonly string[] Anonym =
    [
        "GET /api/betrieb/antiforgery",
        "GET /api/freizeiten/gast/{gastToken}",
        "GET /api/freizeiten/oeffentlich/{token}",
        "GET /api/freizeiten/oeffentlich/{token}/vertrauensseite",
        // Story 2-7: öffentliche Dokumente über den Anmeldelink und die Elterninfo über den Gast-Link.
        "GET /api/freizeiten/oeffentlich/{token}/dokumente/{dokumentId:guid}",
        "GET /api/freizeiten/gast/{gastToken}/anmeldungen/{anmeldungId:guid}/elterninfo",
        "GET /api/freizeiten/gast/{gastToken}/anmeldungen/{anmeldungId:guid}/elterninfo/{dokumentId:guid}",
        "POST /api/betrieb/fehler",
        "POST /api/freizeiten/oeffentlich/{token}/anmeldungen",
        "POST /api/personen/oeffentlich/gaeste",
        "POST /api/personen/oeffentlich/gaeste/{gastToken}/kinder",
        "GET /api/personen/oeffentlich/gaeste/{gastToken}/freizeitpass/{personId:guid}",
        "POST /api/personen/oeffentlich/gaeste/{gastToken}/freizeitpass/{personId:guid}",
        "POST /api/konten/anmelden",
        "POST /api/konten/login-code/einloesen",
        "POST /api/konten/login-link",
        "POST /api/konten/login-link/einloesen",
    ];

    /// <summary>Die personenbezogenen Endpunkte über Organisationen hinweg (AD-26), namentlich.</summary>
    private static readonly string[] Organisationsuebergreifend =
    [
        "GET /api/ich/anmeldungen",
        // Story 2-7: Elterninfo je Anmeldung (die Person selbst oder Eltern ihrer Familie).
        "GET /api/ich/anmeldungen/{anmeldungId:guid}/elterninfo",
        "GET /api/ich/anmeldungen/{anmeldungId:guid}/elterninfo/{dokumentId:guid}",
        "GET /api/ich/daten",
        "GET /api/ich/familie",
        "GET /api/ich/freizeiten",
        "GET /api/ich/freizeitpass/{personId:guid}",
        "GET /api/ich/konto",
        "GET /api/ich/nachweise",
        "GET /api/ich/organisationen",
        "GET /api/ich/organisationen/nachweis-bestaetigungen",
        "GET /api/ich/organisationen/namen",
        "GET /api/ich/profil",
        "GET /api/ich/zugriffsprotokoll",
        "PATCH /api/ich/familie/kinder/{personId:guid}",
        "PATCH /api/ich/freizeitpass/{personId:guid}/antworten/{kennung}",
        "PATCH /api/ich/nachweise/{nachweisId:guid}",
        "PATCH /api/ich/profil",
        "POST /api/ich/anmeldungen",
        "POST /api/ich/familie/einladungen",
        "POST /api/ich/familie/kinder",
        "POST /api/ich/familie/kinder/{personId:guid}/uebergabe",
        "POST /api/ich/freizeitpass/{personId:guid}/antworten/{kennung}/bestaetigen",
        "POST /api/ich/gastprofil/uebernehmen",
        "POST /api/ich/nachweise",
        "POST /api/ich/nachweise/einsichten/{einsichtId:guid}/freigeben",
        "POST /api/ich/nachweise/{nachweisId:guid}/datei",
        "POST /api/ich/profil",
        "POST /api/ich/profil/loeschen",
        "PUT /api/ich/konto/passwort",
    ];

    [Fact]
    public async Task Nur_die_anonyme_Liste_kommt_ohne_Sitzung_aus()
    {
        var endpunkte = await EndpunkteAsync();

        var anonym = endpunkte.Where(e => e.Endpunkt.Metadata.GetMetadata<IAllowAnonymous>() is not null).Select(e => e.Name).Order(StringComparer.Ordinal);
        Assert.Equal(Anonym.Order(StringComparer.Ordinal), anonym);
        Assert.All(
            endpunkte.Where(e => !Anonym.Contains(e.Name)),
            e => Assert.NotEmpty(e.Endpunkt.Metadata.GetOrderedMetadata<IAuthorizeData>()));
    }

    [Fact]
    public async Task Die_Anmelde_Endpunkte_sind_zugriffsbegrenzt()
    {
        var endpunkte = await EndpunkteAsync();

        Assert.All(
            endpunkte.Where(e => e.Name.StartsWith("POST /api/konten/", StringComparison.Ordinal) && Anonym.Contains(e.Name)),
            e => Assert.Contains(
                e.Endpunkt.Metadata.OfType<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>(),
                r => r.PolicyName == Zugriffsbegrenzung.Richtlinie));
        Assert.Equal(4, endpunkte.Count(e => e.Name.StartsWith("POST /api/konten/", StringComparison.Ordinal) && Anonym.Contains(e.Name)));
    }

    [Fact]
    public async Task Die_Anmeldelink_Endpunkte_sind_zugriffsbegrenzt_und_tragen_keine_freizeitId()
    {
        var endpunkte = await EndpunkteAsync();

        // Story 2-2: dazu der Gast-Link, das Absenden der Gast-Anmeldung und die zwei Gast-Endpunkte von Personen; Story 2-4: der
        // Freizeitpass über den Gast-Link (Lesen und Speichern).
        var oeffentlich = endpunkte.Where(e => e.Pfad.StartsWith("/api/freizeiten/oeffentlich/", StringComparison.Ordinal)
                                               || e.Pfad.StartsWith("/api/freizeiten/gast/", StringComparison.Ordinal)
                                               || e.Pfad.StartsWith("/api/personen/oeffentlich/", StringComparison.Ordinal)).ToList();
        // Story 2-7: dazu die öffentliche Datei eines Dokuments und die zwei Endpunkte der Elterninfo über den Gast-Link.
        Assert.Equal(11, oeffentlich.Count);
        Assert.All(oeffentlich, e =>
        {
            Assert.Contains(e.Name, Anonym);
            Assert.Contains(
                e.Endpunkt.Metadata.OfType<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>(),
                r => r.PolicyName == Zugriffsbegrenzung.Richtlinie);
            // Der Mandant kommt aus dem Token, nie aus einer Freizeit in der Route (AD-6).
            Assert.DoesNotContain(e.Endpunkt.RoutePattern.Parameters, p => p.Name == Rechtemerkmale.FreizeitRouteWert || p.Name == Rechtemerkmale.OrgRouteWert);
            Assert.Null(e.Endpunkt.Metadata.GetMetadata<NurFreizeitRolleMetadaten>());
        });
    }

    [Fact]
    public async Task Annehmen_der_Org_Einladung_ist_angemeldet_und_zugriffsbegrenzt()
    {
        var endpunkte = await EndpunkteAsync();

        var annehmen = Assert.Single(endpunkte, e => e.Name == "POST /api/organisationen/einladungen/annehmen");
        Assert.Contains(
            annehmen.Endpunkt.Metadata.OfType<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>(),
            r => r.PolicyName == Zugriffsbegrenzung.Richtlinie);
        Assert.NotEmpty(annehmen.Endpunkt.Metadata.GetOrderedMetadata<IAuthorizeData>());
    }

    [Fact]
    public async Task Annehmen_der_Leitungs_Einladung_ist_angemeldet_und_zugriffsbegrenzt()
    {
        var endpunkte = await EndpunkteAsync();

        var annehmen = Assert.Single(endpunkte, e => e.Name == "POST /api/freizeiten/leitung/annehmen");
        Assert.Contains(
            annehmen.Endpunkt.Metadata.OfType<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>(),
            r => r.PolicyName == Zugriffsbegrenzung.Richtlinie);
        Assert.NotEmpty(annehmen.Endpunkt.Metadata.GetOrderedMetadata<IAuthorizeData>());
    }

    [Theory]
    [InlineData("POST /api/personen/familie/einladungen/annehmen")]
    [InlineData("POST /api/personen/uebergabe/annehmen")]
    [InlineData("POST /api/ich/gastprofil/uebernehmen")]
    [InlineData("POST /api/ich/anmeldungen")]
    public async Task Annehmen_von_Familien_Einladung_und_Profil_Uebergabe_ist_angemeldet_und_zugriffsbegrenzt(string name)
    {
        var endpunkte = await EndpunkteAsync();

        var annehmen = Assert.Single(endpunkte, e => e.Name == name);
        Assert.Contains(
            annehmen.Endpunkt.Metadata.OfType<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>(),
            r => r.PolicyName == Zugriffsbegrenzung.Richtlinie);
        Assert.NotEmpty(annehmen.Endpunkt.Metadata.GetOrderedMetadata<IAuthorizeData>());
    }

    [Fact]
    public async Task Nur_Profil_Uebergabe_Datenauskunft_und_Konto_loeschen_verlangen_die_erneute_Anmeldung()
    {
        var endpunkte = await EndpunkteAsync();

        // AD-25: vor Export, Konto-Löschung und Profil-Übergabe (Story 1-7: Auskunft und Löschen).
        Assert.Equal(
            ["GET /api/ich/daten", "POST /api/ich/familie/kinder/{personId:guid}/uebergabe", "POST /api/ich/profil/loeschen"],
            endpunkte.Where(e => e.Endpunkt.Metadata.GetMetadata<Zeltlotse.SharedKernel.Anmeldung.ErneuteAnmeldungMetadaten>() is not null)
                .Select(e => e.Name)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Jede_Freizeit_Route_traegt_NurFreizeitRolle_und_die_Guid_Einschraenkung_und_kein_Endpunkt_beide_Merkmale()
    {
        var endpunkte = await EndpunkteAsync();

        // Freizeit-Routen: freizeitId ohne orgId. Org-Routen mit freizeitId (Verwaltung) prüfen die Org-Rolle.
        var mitFreizeit = endpunkte.Where(e => e.Endpunkt.RoutePattern.Parameters.Any(p => p.Name == Rechtemerkmale.FreizeitRouteWert)).ToList();
        var freizeitRouten = mitFreizeit.Where(e => e.Endpunkt.RoutePattern.Parameters.All(p => p.Name != Rechtemerkmale.OrgRouteWert)).ToList();
        // Story 1-5: die sieben Prüf-Endpunkte der Leitung (Personen).
        const string pruefen = "/api/freizeiten/{freizeitId:guid}/personen/nachweise";
        Assert.Equal(
            new[]
            {
                "GET /api/freizeiten/{freizeitId:guid}",
                "GET /api/freizeiten/{freizeitId:guid}/kontext",
                // Story 1-6: Warnhinweise (Freizeiten) und Erinnern (Personen).
                "GET /api/freizeiten/{freizeitId:guid}/nachweis-warnungen",
                $"POST {pruefen}/erinnerungen",
                $"GET {pruefen}",
                $"GET {pruefen}/einsichten/{{einsichtId:guid}}",
                $"GET {pruefen}/einsichten/{{einsichtId:guid}}/nachweise/{{nachweisId:guid}}/datei",
                $"POST {pruefen}/anfragen",
                $"POST {pruefen}/einsichten/{{einsichtId:guid}}/nachweise/{{nachweisId:guid}}/ablehnen",
                $"POST {pruefen}/einsichten/{{einsichtId:guid}}/nachweise/{{nachweisId:guid}}/bestaetigen",
                $"POST {pruefen}/einsichten/{{einsichtId:guid}}/pruefen",
                // Story 2-1: Einstellungen der Freizeitseite und Anmeldelinks (Leitung).
                "GET /api/freizeiten/{freizeitId:guid}/freizeitseite",
                "PATCH /api/freizeiten/{freizeitId:guid}/freizeitseite",
                "POST /api/freizeiten/{freizeitId:guid}/anmeldelinks/{art}/erneuern",
                // Story 2-3: Anmeldungen der Leitung (Liste, Bestätigen, Absagen).
                "GET /api/freizeiten/{freizeitId:guid}/anmeldungen",
                "POST /api/freizeiten/{freizeitId:guid}/anmeldungen/{anmeldungId:guid}/bestaetigen",
                "POST /api/freizeiten/{freizeitId:guid}/anmeldungen/{anmeldungId:guid}/absagen",
                // Story 2-4: Fragen-Auswahl des Freizeitpasses (Leitung).
                "GET /api/freizeiten/{freizeitId:guid}/fragen",
                "PATCH /api/freizeiten/{freizeitId:guid}/fragen",
                // Story 3-7: Module der Freizeit (Leitung).
                "GET /api/freizeiten/{freizeitId:guid}/module",
                "PATCH /api/freizeiten/{freizeitId:guid}/module",
                // Story 3-8: Freizeit abschließen (Leitung).
                "POST /api/freizeiten/{freizeitId:guid}/abschliessen",
                // Story 2-5: Freizeitpass im Freizeit-Kontext (Personen), Personenliste und Team-Notizen (Freizeiten), Leitung und Team.
                "GET /api/freizeiten/{freizeitId:guid}/personen/freizeitpass/{personId:guid}",
                "GET /api/freizeiten/{freizeitId:guid}/personenliste",
                "GET /api/freizeiten/{freizeitId:guid}/anmeldungen/{anmeldungId:guid}/notizen",
                "POST /api/freizeiten/{freizeitId:guid}/anmeldungen/{anmeldungId:guid}/notizen",
                "DELETE /api/freizeiten/{freizeitId:guid}/anmeldungen/{anmeldungId:guid}/notizen/{notizId:guid}",
                // Story 2-6: Freizeitpass-Stand und Erinnern zum Freizeitpass (Personen, Leitung).
                "GET /api/freizeiten/{freizeitId:guid}/personen/freizeitpass-stand",
                "POST /api/freizeiten/{freizeitId:guid}/personen/freizeitpass/erinnerungen",
                // Story 2-11: Notfallordner, die Pässe bestätigter Personen gebündelt (Personen, Leitung und Team).
                "GET /api/freizeiten/{freizeitId:guid}/personen/notfallordner",
                // Story 2-7: Dokumente der Freizeit und Einbinden von Org-Dokumenten (Leitung und Team).
                "GET /api/freizeiten/{freizeitId:guid}/dokumente",
                "POST /api/freizeiten/{freizeitId:guid}/dokumente",
                "PATCH /api/freizeiten/{freizeitId:guid}/dokumente/{dokumentId:guid}",
                "DELETE /api/freizeiten/{freizeitId:guid}/dokumente/{dokumentId:guid}",
                "GET /api/freizeiten/{freizeitId:guid}/dokumente/{dokumentId:guid}/datei",
                "GET /api/freizeiten/{freizeitId:guid}/dokumente/org",
                "POST /api/freizeiten/{freizeitId:guid}/dokumente/einbindungen",
                // Story 2-8: Sicherheitskette (Sicherheit): lesen für Leitung und Team, einrichten nur Leitung.
                "GET /api/freizeiten/{freizeitId:guid}/sicherheit/sicherheitskette",
                "PATCH /api/freizeiten/{freizeitId:guid}/sicherheit/sicherheitskette",
                // Story 2-9: Schutzkonzept (Sicherheit): lesen und bestätigen für Leitung und Team, Dokument wählen nur Leitung.
                "GET /api/freizeiten/{freizeitId:guid}/sicherheit/schutzkonzept",
                "PATCH /api/freizeiten/{freizeitId:guid}/sicherheit/schutzkonzept",
                "POST /api/freizeiten/{freizeitId:guid}/sicherheit/schutzkonzept/gelesen",
                // Story 2-10: Vorfall-Protokoll (Sicherheit): lesen und Einträge anlegen für Leitung und Team.
                "GET /api/freizeiten/{freizeitId:guid}/sicherheit/vorfaelle",
                "POST /api/freizeiten/{freizeitId:guid}/sicherheit/vorfaelle",
                // Story 3-1: Wochenraster und Programmpunkte (Programm), alles für Leitung und Team.
                "GET /api/freizeiten/{freizeitId:guid}/programm",
                "POST /api/freizeiten/{freizeitId:guid}/programm/punkte",
                "PATCH /api/freizeiten/{freizeitId:guid}/programm/punkte/{punktId:guid}",
                "DELETE /api/freizeiten/{freizeitId:guid}/programm/punkte/{punktId:guid}",
                "POST /api/freizeiten/{freizeitId:guid}/programm/punkte/{punktId:guid}/uebernehmen",
                "POST /api/freizeiten/{freizeitId:guid}/programm/punkte/{punktId:guid}/abgeben",
                // Story 6-2: Verschiebung mitgeteilt oder Hinweis ausgeblendet (Programm), für Leitung und Team.
                "POST /api/freizeiten/{freizeitId:guid}/programm/punkte/{punktId:guid}/mitgeteilt",
                // Story 3-2: Material am Programmpunkt (Programm), alles für Leitung und Team.
                "GET /api/freizeiten/{freizeitId:guid}/programm/material",
                "POST /api/freizeiten/{freizeitId:guid}/programm/punkte/{punktId:guid}/material",
                "PATCH /api/freizeiten/{freizeitId:guid}/programm/material/{materialId:guid}",
                "DELETE /api/freizeiten/{freizeitId:guid}/programm/material/{materialId:guid}",
                "POST /api/freizeiten/{freizeitId:guid}/programm/material/{materialId:guid}/status",
                "POST /api/freizeiten/{freizeitId:guid}/programm/material/{materialId:guid}/mitbringen",
                "POST /api/freizeiten/{freizeitId:guid}/programm/material/{materialId:guid}/nicht-mitbringen",
                // Story 3-3: Inventar in der Freizeit (Inventar), lesen, reservieren und aufheben für Leitung und Team.
                "GET /api/freizeiten/{freizeitId:guid}/inventar",
                "POST /api/freizeiten/{freizeitId:guid}/inventar/{artikelId:guid}/reservierungen",
                "DELETE /api/freizeiten/{freizeitId:guid}/inventar/reservierungen/{reservierungId:guid}",
                // Story 3-4: Schadens- und Verlustliste (Inventar), alles für Leitung und Team, auch nach dem Abschluss.
                "GET /api/freizeiten/{freizeitId:guid}/inventar/schaeden",
                "POST /api/freizeiten/{freizeitId:guid}/inventar/schaeden",
                "PATCH /api/freizeiten/{freizeitId:guid}/inventar/schaeden/{eintragId:guid}",
                "DELETE /api/freizeiten/{freizeitId:guid}/inventar/schaeden/{eintragId:guid}",
                // Story 3-5: Aufgaben (Programm), alles für Leitung und Team.
                "GET /api/freizeiten/{freizeitId:guid}/programm/aufgaben",
                "POST /api/freizeiten/{freizeitId:guid}/programm/aufgaben",
                "PATCH /api/freizeiten/{freizeitId:guid}/programm/aufgaben/{aufgabeId:guid}",
                "DELETE /api/freizeiten/{freizeitId:guid}/programm/aufgaben/{aufgabeId:guid}",
                "POST /api/freizeiten/{freizeitId:guid}/programm/aufgaben/{aufgabeId:guid}/uebernehmen",
                "POST /api/freizeiten/{freizeitId:guid}/programm/aufgaben/{aufgabeId:guid}/abgeben",
                "POST /api/freizeiten/{freizeitId:guid}/programm/aufgaben/{aufgabeId:guid}/erledigt",
                "POST /api/freizeiten/{freizeitId:guid}/programm/aufgaben/{aufgabeId:guid}/offen",
                "POST /api/freizeiten/{freizeitId:guid}/programm/aufgaben/{aufgabeId:guid}/vorlage",
                // Story 3-6: Programmbibliothek (Programm): ablegen, lesen und übernehmen für Leitung und Team.
                "POST /api/freizeiten/{freizeitId:guid}/programm/punkte/{punktId:guid}/bibliothek",
                "GET /api/freizeiten/{freizeitId:guid}/programm/bibliothek",
                "POST /api/freizeiten/{freizeitId:guid}/programm/bibliothek/{eintragId:guid}/uebernehmen",
                // Story 4-1: Rezepte (Küche), alles für Leitung und Team, nur mit dem Modul Verpflegung.
                "GET /api/freizeiten/{freizeitId:guid}/kueche/rezepte",
                "POST /api/freizeiten/{freizeitId:guid}/kueche/rezepte",
                "GET /api/freizeiten/{freizeitId:guid}/kueche/rezepte/{rezeptId:guid}",
                "PATCH /api/freizeiten/{freizeitId:guid}/kueche/rezepte/{rezeptId:guid}",
                "DELETE /api/freizeiten/{freizeitId:guid}/kueche/rezepte/{rezeptId:guid}",
                "POST /api/freizeiten/{freizeitId:guid}/kueche/rezepte/{rezeptId:guid}/zutaten",
                "PATCH /api/freizeiten/{freizeitId:guid}/kueche/rezepte/{rezeptId:guid}/zutaten/{zutatId:guid}",
                "DELETE /api/freizeiten/{freizeitId:guid}/kueche/rezepte/{rezeptId:guid}/zutaten/{zutatId:guid}",
                // Story 4-2: Essensplan, Gerichte und Einkaufsliste (Küche), alles für Leitung und Team, nur mit dem Modul Verpflegung.
                "GET /api/freizeiten/{freizeitId:guid}/kueche/essensplan",
                "POST /api/freizeiten/{freizeitId:guid}/kueche/gerichte",
                "DELETE /api/freizeiten/{freizeitId:guid}/kueche/gerichte/{gerichtId:guid}",
                "GET /api/freizeiten/{freizeitId:guid}/kueche/gerichte/{gerichtId:guid}/tausch",
                "POST /api/freizeiten/{freizeitId:guid}/kueche/gerichte/{gerichtId:guid}/tauschen",
                "GET /api/freizeiten/{freizeitId:guid}/kueche/einkauf",
                "GET /api/freizeiten/{freizeitId:guid}/kueche/einkaufsliste",
                "POST /api/freizeiten/{freizeitId:guid}/kueche/einkaufsliste/freigabe",
                "PATCH /api/freizeiten/{freizeitId:guid}/kueche/einkaufsliste/gerichte/{gerichtId:guid}/zutaten/{zutatId:guid}",
                "POST /api/freizeiten/{freizeitId:guid}/kueche/einkaufsliste/posten",
                "PATCH /api/freizeiten/{freizeitId:guid}/kueche/einkaufsliste/posten/{postenId:guid}",
                "DELETE /api/freizeiten/{freizeitId:guid}/kueche/einkaufsliste/posten/{postenId:guid}",
                // Story 4-4: Küchen-Tagesansicht und Restetag.
                "PATCH /api/freizeiten/{freizeitId:guid}/kueche/mahlzeiten/{programmpunktId:guid}",
                "GET /api/freizeiten/{freizeitId:guid}/kueche/tagesansicht",
                // Story 5-1: Kalkulation (Finanzen), alles für Leitung und Team, nur mit dem Modul Finanzen.
                "GET /api/freizeiten/{freizeitId:guid}/finanzen/kalkulation",
                "PATCH /api/freizeiten/{freizeitId:guid}/finanzen/kalkulation",
                "PATCH /api/freizeiten/{freizeitId:guid}/finanzen/kalkulation/kategorien/{kategorie}",
                "POST /api/freizeiten/{freizeitId:guid}/finanzen/kalkulation/einnahmen",
                "PATCH /api/freizeiten/{freizeitId:guid}/finanzen/kalkulation/einnahmen/{einnahmeId:guid}",
                "DELETE /api/freizeiten/{freizeitId:guid}/finanzen/kalkulation/einnahmen/{einnahmeId:guid}",
                // Story 5-2: Belege, Erstattungsvermerk, Datei und Soll-Ist (Finanzen), alles für Leitung und Team, nur mit dem Modul Finanzen.
                "GET /api/freizeiten/{freizeitId:guid}/finanzen/belege",
                "POST /api/freizeiten/{freizeitId:guid}/finanzen/belege",
                "PATCH /api/freizeiten/{freizeitId:guid}/finanzen/belege/{belegId:guid}",
                "DELETE /api/freizeiten/{freizeitId:guid}/finanzen/belege/{belegId:guid}",
                "PATCH /api/freizeiten/{freizeitId:guid}/finanzen/belege/{belegId:guid}/erstattet",
                "POST /api/freizeiten/{freizeitId:guid}/finanzen/belege/{belegId:guid}/datei",
                "GET /api/freizeiten/{freizeitId:guid}/finanzen/belege/{belegId:guid}/datei",
                "DELETE /api/freizeiten/{freizeitId:guid}/finanzen/belege/{belegId:guid}/datei",
                "GET /api/freizeiten/{freizeitId:guid}/finanzen/soll-ist",
                // Story 5-3: Beitrag bezahlt abhaken und wieder öffnen (Finanzen), für Leitung und Team, nur mit dem Modul Finanzen.
                "GET /api/freizeiten/{freizeitId:guid}/finanzen/beitraege",
                "POST /api/freizeiten/{freizeitId:guid}/finanzen/beitraege/{personId:guid}/bezahlt",
                "POST /api/freizeiten/{freizeitId:guid}/finanzen/beitraege/{personId:guid}/offen",
                // Story 6-1: Kanäle „Team“ und „Alle“ und das Push-Abo, für Leitung und Team, ohne Funktions-Schalter.
                "GET /api/freizeiten/{freizeitId:guid}/kanaele/{kanal}/nachrichten",
                "POST /api/freizeiten/{freizeitId:guid}/kanaele/{kanal}/nachrichten",
                "GET /api/freizeiten/{freizeitId:guid}/kanaele/push/schluessel",
                "POST /api/freizeiten/{freizeitId:guid}/kanaele/push/abo",
                "POST /api/freizeiten/{freizeitId:guid}/kanaele/push/abo/abmelden",
            }.Order(StringComparer.Ordinal),
            freizeitRouten.Select(e => e.Name).Order(StringComparer.Ordinal));
        Assert.All(mitFreizeit, e => Assert.Contains("{freizeitId:guid}", e.Pfad, StringComparison.Ordinal));
        Assert.All(freizeitRouten, e => Assert.NotNull(e.Endpunkt.Metadata.GetMetadata<NurFreizeitRolleMetadaten>()));
        Assert.All(
            endpunkte.Where(e => e.Endpunkt.Metadata.GetMetadata<NurFreizeitRolleMetadaten>() is not null),
            e => Assert.Contains(e, freizeitRouten));
        Assert.DoesNotContain(
            endpunkte,
            e => e.Endpunkt.Metadata.GetMetadata<NurFreizeitRolleMetadaten>() is not null && e.Endpunkt.Metadata.GetMetadata<NurOrgRolleMetadaten>() is not null);
    }

    [Fact]
    public async Task Jeder_Endpunkt_von_Kueche_und_Finanzen_gehoert_zu_seinem_Schalter_und_jeder_Schalter_Endpunkt_prueft_die_Freizeit_Rolle()
    {
        var endpunkte = await EndpunkteAsync();

        // Story 3-7 (AD-20): Leitplanke für V4 und V5: Jeder Endpunkt dort erklärt seinen Schalter. Story 4-1: die acht Routen der Rezepte;
        // Story 4-2: zehn Routen für Essensplan, Gerichte und Einkaufsliste; Story 4-3: Tages-Freigabe und die Seite der Einkaufenden.
        Assert.Equal(22, endpunkte.Count(e => e.Pfad.Contains("/kueche/", StringComparison.Ordinal)));
        // Story 5-1: die sechs Routen der Kalkulation; Story 5-2: die neun Routen der Belege und des Soll-Ist-Vergleichs; Story 5-3: die drei Routen der Beiträge.
        Assert.Equal(18, endpunkte.Count(e => e.Pfad.Contains("/finanzen/", StringComparison.Ordinal)));
        foreach (var (bereich, schalter) in new[] { ("kueche", FunktionsSchalter.Verpflegung), ("finanzen", FunktionsSchalter.Finanzen) })
        {
            var praefix = $"/api/freizeiten/{{{Rechtemerkmale.FreizeitRouteWert}:guid}}/{bereich}";
            Assert.All(
                endpunkte.Where(e => e.Pfad == praefix || e.Pfad.StartsWith(praefix + "/", StringComparison.Ordinal)),
                e => Assert.Equal(schalter, e.Endpunkt.Metadata.GetMetadata<GehoertZuSchalterMetadaten>()?.Schalter));
        }

        // Story 6-1 (AD-20): Die Kanäle gehören zu keinem Schalter; der Team-Kanal ist nie abschaltbar.
        var kanaele = endpunkte.Where(e => e.Pfad.Contains("/kanaele/", StringComparison.Ordinal)).ToList();
        Assert.Equal(5, kanaele.Count);
        Assert.All(kanaele, e => Assert.Null(e.Endpunkt.Metadata.GetMetadata<GehoertZuSchalterMetadaten>()));

        // Ein Schalter ohne Freizeit-Rolle wäre ein Konfigurationsfehler (die Rechteprüfung wirft).
        Assert.All(
            endpunkte.Where(e => e.Endpunkt.Metadata.GetMetadata<GehoertZuSchalterMetadaten>() is not null),
            e => Assert.NotNull(e.Endpunkt.Metadata.GetMetadata<NurFreizeitRolleMetadaten>()));
    }

    [Fact]
    public async Task Jede_Route_mit_orgId_traegt_NurOrgRolle_und_die_Guid_Einschraenkung()
    {
        var endpunkte = await EndpunkteAsync();

        var mitOrg = endpunkte.Where(e => e.Endpunkt.RoutePattern.Parameters.Any(p => p.Name == Rechtemerkmale.OrgRouteWert)).ToList();
        // Story 1-5: dazu die sieben Prüf-Endpunkte des Org-Admins und die zwei der Nachweis-Richtlinie; Story 1-7: die zwei der Löschfristen.
        // Story 2-7: die vier Endpunkte der Org-Dokumente; Story 2-10: das Archiv der Vorfall-Notizen; Story 3-3: die vier des Inventars;
        // Story 3-4: die zwei der Anschaffungswünsche; Story 3-5: die vier der Aufgaben-Vorlagen; Story 3-6: die zwei der Programmbibliothek.
        Assert.Equal(35, mitOrg.Count);
        Assert.Contains(mitOrg, e => e.Name == "DELETE /api/organisationen/{orgId:guid}/programmbibliothek/{eintragId:guid}");
        Assert.Contains(mitOrg, e => e.Name == "GET /api/organisationen/{orgId:guid}/programmbibliothek");
        Assert.Contains(mitOrg, e => e.Name == "PATCH /api/organisationen/{orgId:guid}/aufgaben-vorlagen/{vorlageId:guid}");
        Assert.Contains(mitOrg, e => e.Name == "DELETE /api/organisationen/{orgId:guid}/inventar/anschaffungswuensche/{eintragId:guid}");
        Assert.Contains(mitOrg, e => e.Name == "PATCH /api/organisationen/{orgId:guid}/inventar/{artikelId:guid}");
        Assert.Contains(mitOrg, e => e.Name == "GET /api/organisationen/{orgId:guid}/sicherheit/vorfall-archiv");
        Assert.All(mitOrg, e =>
        {
            Assert.NotNull(e.Endpunkt.Metadata.GetMetadata<NurOrgRolleMetadaten>());
            Assert.Contains("{orgId:guid}", e.Pfad, StringComparison.Ordinal);
        });
        Assert.All(
            endpunkte.Where(e => e.Endpunkt.Metadata.GetMetadata<NurOrgRolleMetadaten>() is not null),
            e => Assert.Contains(e, mitOrg));
        Assert.Equal(
            ["GET /api/organisationen", "POST /api/organisationen"],
            endpunkte.Where(e => e.Endpunkt.Metadata.GetMetadata<NurGlobalAdminMetadaten>() is not null).Select(e => e.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Endpunkte_unter_api_ich_sind_namentlich_bekannt_und_organisationsuebergreifend_markiert()
    {
        var endpunkte = await EndpunkteAsync();

        var markiert = endpunkte.Where(e => e.Endpunkt.Metadata.GetMetadata<OrganisationsuebergreifendMetadaten>() is not null).Select(e => e.Name);
        var unterIch = endpunkte.Where(e => e.Pfad.StartsWith("/api/ich/", StringComparison.Ordinal) || e.Pfad == "/api/ich").Select(e => e.Name);
        Assert.Equal(Organisationsuebergreifend.Order(StringComparer.Ordinal), markiert.Order(StringComparer.Ordinal));
        Assert.Equal(Organisationsuebergreifend.Order(StringComparer.Ordinal), unterIch.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Jeder_angemeldete_Endpunkt_antwortet_ohne_Sitzung_mit_401_ProblemDetails()
    {
        await using var factory = Factory(await postgres.NeueMigrierteDatenbankAsync());
        using var client = factory.CreateClient();
        var endpunkte = Endpunkte(factory).Where(e => !Anonym.Contains(e.Name)).ToList();
        Assert.NotEmpty(endpunkte);

        foreach (var (name, methode, pfad, _) in endpunkte)
        {
            // Routenwerte (z. B. {orgId:guid}) bekommen eine Kennung, damit die Route passt.
            var aufruf = System.Text.RegularExpressions.Regex.Replace(pfad, @"\{[^}]+\}", Guid.CreateVersion7().ToString());
            using var antwort = await SendenAsync(client, new HttpMethod(methode), aufruf, methode == "GET" ? null : "{}");

            Assert.True(antwort.StatusCode == HttpStatusCode.Unauthorized, $"{name}: {antwort.StatusCode}");
            Assert.Equal("application/problem+json", antwort.Content.Headers.ContentType?.MediaType);
            Assert.Null(antwort.Headers.Location);
        }
    }

    private async Task<List<(string Name, string Methode, string Pfad, RouteEndpoint Endpunkt)>> EndpunkteAsync()
    {
        var factory = new ZeltlotseApiFactory(await postgres.NeueMigrierteDatenbankAsync());
        await using (factory)
        {
            _ = factory.CreateClient();
            return Endpunkte(factory);
        }
    }

    private static List<(string Name, string Methode, string Pfad, RouteEndpoint Endpunkt)> Endpunkte(ZeltlotseApiFactory factory) =>
    [
        .. factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Select(e => (Endpunkt: e, Pfad: "/" + (e.RoutePattern.RawText ?? "").Trim('/')))
            .Where(e => e.Pfad == "/api" || (e.Pfad.StartsWith("/api/", StringComparison.Ordinal) && !e.Pfad.StartsWith("/api/test/", StringComparison.Ordinal)))
            .SelectMany(e => (e.Endpunkt.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.IHttpMethodMetadata>()?.HttpMethods ?? ["*"])
                .Select(m => ($"{m} {e.Pfad}", m, e.Pfad, e.Endpunkt))),
    ];
}
