using System.Globalization;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Freizeiten.Anmeldungen;
using Zeltlotse.Freizeiten.Anmeldungen.Entscheiden;
using Zeltlotse.Freizeiten.Daten;
using Zeltlotse.Freizeiten.Dokumente;
using Zeltlotse.Freizeiten.Domaene;
using Zeltlotse.Freizeiten.Fragen;
using Zeltlotse.Freizeiten.Freizeitseite;
using Zeltlotse.Freizeiten.Leitung;
using Zeltlotse.Freizeiten.TeamNotizen;
using Zeltlotse.Freizeiten.Verwaltung;
using Zeltlotse.Inventar.Artikel;
using Zeltlotse.Inventar.Daten;
using Zeltlotse.Inventar.Reservierungen;
using Zeltlotse.Inventar.Schaeden;
using Zeltlotse.Konten.Anmelden;
using Zeltlotse.Organisationen.Daten;
using Zeltlotse.Organisationen.Dokumente;
using Zeltlotse.Organisationen.Einladungen;
using Zeltlotse.Organisationen.Nachweise;
using Zeltlotse.Organisationen.Verwaltung;
using Zeltlotse.Personen.Daten;
using Zeltlotse.Personen.Familien;
using Zeltlotse.Personen.Freizeitpass;
using Zeltlotse.Personen.Nachweise;
using Zeltlotse.Personen.Profil;
using Zeltlotse.Personen.Zugriffsprotokoll;
using Zeltlotse.Programm.Aufgaben;
using Zeltlotse.Programm.Daten;
using Zeltlotse.Programm.Materialien;
using Zeltlotse.Programm.Programme;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Outbox;
using Zeltlotse.SharedKernel.Rechte;
using Zeltlotse.Api.Contracts.Sicherheit;
using Zeltlotse.Sicherheit.Daten;
using Zeltlotse.Sicherheit.Schutzkonzept;
using Zeltlotse.Sicherheit.Sicherheitskette;
using Zeltlotse.Sicherheit.Vorfaelle;
using EinsichtStatus = Zeltlotse.Personen.Nachweise.EinsichtStatus;
using NachweisArt = Zeltlotse.Personen.Contracts.Events.NachweisArt;

namespace Zeltlotse.Api.Demo;

/// <summary>
/// Spielt die erfundenen Demo-Daten aus <see cref="DemoKatalog"/> ein (NFR-20, Konvention „Anwender-Doku“), wenn der
/// Schalter <see cref="Schalter"/> an ist. Erlaubt ist das nur in <c>Development</c> und <c>Testing</c>; das prüft
/// <see cref="Start.Startpruefung"/> vor den Migrationen. Der Host ruft <see cref="EinspielenAsync"/> direkt nach den
/// Migrationen auf; wiederholtes Einspielen (Neustart mit derselben Datenbank) ist unschädlich.
/// </summary>
/// <remarks>
/// Eingespielt wird nur über die Commands der Module (Mediator, AD-27), nie direkt in Tabellen. Jede Fach-Story ergänzt
/// hier ihre Commands und macht sie idempotent. Bisher (Story 1-1): je Demo-Person mit Mail ein Konto und ein Profil mit
/// Vor- und Nachnamen; eine vorhandene Mail-Adresse wird übersprungen. Kinder ohne Mail bekommen hier kein Konto (Familie: Story 1-4).
/// Story 1-2: Fehlt die Demo-Organisation, legt Gerd (Betreiber) sie an und lädt Andrea ein; Andrea nimmt an (Org-Admin) und
/// lädt <see cref="DemoKatalog.UeberblickEinladung"/> als Org-Überblick ein (bleibt offen). Gerd wird Global Admin erst im
/// Abgleich danach (<see cref="Start.GlobalAdmins"/>).
/// Story 1-3: Fehlt in der Demo-Organisation die Freizeit <see cref="DemoKatalog.Freizeit"/>, legt Andrea sie an und lädt
/// die Personen mit <see cref="DemoRolle.Leitung"/> ein; jede nimmt ihre Einladung per Command an. Die Ereignisse stellt der
/// normale Zustelldienst zu.
/// Story 1-4: Je Demo-Person mit <see cref="DemoRolle.Eltern"/> entstehen die Kinder ihrer Familie per Command (Katrin: Paul und
/// Emma, Daniel: Mia); ein Kind mit gleichem Vornamen in der Familie wird übersprungen.
/// Story 1-5: Jonas' Nachweise mit Demo-Dateien (der Dateispeicher muss konfiguriert sein), automatische Einsicht für Jonas und
/// Miriam, Jonas gibt frei, Miriam bestätigt Erste Hilfe.
/// Story 1-6: Andrea setzt in der Nachweis-Richtlinie „Erste Hilfe“ auf Pflicht (nur, wenn es noch keine Pflicht ist). Damit
/// zeigt die Freizeit Warnhinweise; eine Demo-Erinnerung gibt es nicht, also verschickt das Einspielen keine Mails.
/// Story 1-7: Miriam hat Jonas' Nachweise eingesehen: ein Eintrag im Einsicht-Protokoll (Organisation und Freizeit der Demo), nur
/// wenn es noch keinen Eintrag von Miriam über Jonas gibt. Die Löschfristen der Demo-Organisation haben die Vorgabewerte.
/// Story 2-1: Jonas und Miriam tragen die Herkunft aus dem Katalog; die Demo-Freizeit hat Ort, Beschreibung und Höchstzahl
/// (je nur, wenn das Feld noch leer ist) und ihre beiden Anmeldelinks (fehlt einer, entsteht er per Erneuern).
/// Story 2-2: Die Demo-Freizeit hat Teilnahmebedingungen (nur, wenn leer); Katrin hat Emma mit Konto angemeldet (nur einmal), die
/// Freigabe der Stufe Leitung ist zugestellt.
/// Story 2-3: Die Demo-Freizeit hat den Kontakt für Absagen (nur, wenn leer); Daniel meldet Mia an, Lea und Tobias sich selbst als
/// Mitarbeitende; Miriam bestätigt Mia und Lea (nur, solange eingegangen). Lea wird damit Team der Freizeit und Mitarbeitende der
/// Organisation; Bestätigungs-Mails gehen an Daniel und Lea.
/// Story 2-4: Miriam wählt die Fragen des Freizeitpasses je Zielgruppe (nur, wenn noch keine gewählt sind); Katrin beantwortet für
/// Emma Allergien, Notfallkontakte und Schwimmen (je nur, wenn es noch keine Antwort gibt).
/// Story 2-5: Die Fragen-Auswahl kommt vor den Anmeldungen, damit die Freigaben die Fragen tragen; Daniel beantwortet für Mia
/// Allergien, Notfallkontakte und Schwimmen (je nur ohne Antwort); Lea schreibt eine Team-Notiz zu Mia (nur ohne Notiz).
/// Story 2-11: Daniel trägt für Mia zusätzlich Medikamente ein (nur ohne Antwort), damit der Notfallordner sie zeigt.
/// Story 2-7: zwei Org-Dokumente, fünf Dokumente der Freizeit und die eingebundene Hausordnung (je nur, wenn sie fehlen).
/// Story 2-8: Miriam richtet die Sicherheitskette ein (je Feld nur, wenn es leer ist).
/// Story 2-9: Andrea lädt das Org-Dokument „Schutzkonzept Jugendwerk Musterstadt“ hoch, die Freizeit bindet es ein (Sichtbarkeit Team) und
/// wählt es als Schutzkonzept; Jonas bestätigt es als gelesen (je nur, wenn es fehlt).
/// Story 3-1: Die festen Mahlzeiten der Demo-Freizeit und die Programmpunkte aus <see cref="DemoKatalog.Programmpunkte"/>; Lea und Jonas
/// übernehmen je einen selbst (je nur, wenn es den Titel noch nicht gibt).
/// Story 3-2: Material an Geländespiel und Lagerfeuer aus <see cref="DemoKatalog.Material"/> mit Menge und Status; Miriam bringt die
/// Taschenlampen mit (je nur, wenn es die Bezeichnung am Programmpunkt noch nicht gibt).
/// Story 3-3: die zweite Freizeit <see cref="DemoKatalog.ZweiteFreizeit"/> mit Tim als Leitung (nur, wenn es sie noch nicht gibt), das Inventar
/// aus <see cref="DemoKatalog.Inventar"/> (je nur, wenn es die Bezeichnung noch nicht gibt) und die Reservierungen aus
/// <see cref="DemoKatalog.Reservierungen"/> (je nur, wenn die Freizeit den Artikel noch nicht reserviert hat); die Fahnen hängen am Material
/// „Fahnen“ des Geländespiels.
/// Story 3-4: die Schadens- und Verlustliste der Demo-Freizeit aus <see cref="DemoKatalog.Schaeden"/> (je nur, wenn es die Bezeichnung dort noch nicht
/// gibt); Leas Entnahme im Vorfall-Protokoll (Story 2-10) setzt über das Ereignis <c>ErsteHilfeEntnommen</c> den Eintrag „Erste-Hilfe-Kasten“ dazu,
/// sobald der Zustelldienst es zugestellt hat.
/// </remarks>
internal sealed partial class DemoDaten(IConfiguration konfiguration, IServiceScopeFactory scopes, ILogger<DemoDaten> logger)
{
    public const string Schalter = "Zeltlotse:DemoDaten:Aktiv";

    private int _einspielungen;

    /// <summary>Ob der Schalter an ist (Vorgabe: aus).</summary>
    public bool Aktiv => IstAktiv(konfiguration);

    /// <summary>Wie oft dieser Host die Demo-Daten eingespielt hat.</summary>
    public int Einspielungen => _einspielungen;

    public static bool IstAktiv(IConfiguration konfiguration) => konfiguration.GetValue<bool>(Schalter);

    public async Task EinspielenAsync(CancellationToken abbruch = default)
    {
        abbruch.ThrowIfCancellationRequested();
        if (!Aktiv)
        {
            return;
        }

        var neu = 0;
        foreach (var person in DemoKatalog.Personen.Where(p => p.Mail is not null))
        {
            if (await KontoUndProfilAsync(person, abbruch))
            {
                neu++;
            }
        }

        await OrganisationAsync(abbruch);
        await FreizeitAsync(abbruch);
        foreach (var eltern in DemoKatalog.Personen.Where(p => p.Rolle == DemoRolle.Eltern))
        {
            await KinderAsync(eltern, abbruch);
        }

        await RichtlinieAsync(abbruch);
        await NachweiseAsync(abbruch);
        await ZugriffsprotokollAsync(abbruch);
        await HerkunftAsync(abbruch);
        await FreizeitseiteAsync(abbruch);
        // Story 2-5: Die Fragen-Auswahl kommt vor den Anmeldungen, damit die Freigaben die Fragen tragen.
        await FragenAuswahlAsync(abbruch);
        await AnmeldungAsync(abbruch);
        await FreizeitpassAsync(abbruch);
        await TeamNotizAsync(abbruch);
        await DokumenteAsync(abbruch);
        await SicherheitsketteAsync(abbruch);
        await SchutzkonzeptAsync(abbruch);
        await VorfaelleAsync(abbruch);
        await ProgrammAsync(abbruch);
        await MaterialAsync(abbruch);
        await ZweiteFreizeitAsync(abbruch);
        await InventarAsync(abbruch);
        await SchaedenAsync(abbruch);
        await AufgabenAsync(abbruch);

        Interlocked.Increment(ref _einspielungen);
        Eingespielt(DemoKatalog.Personen.Count, neu);
    }

    /// <summary>Konto und Profil einer Demo-Person; <c>false</c>, wenn es das Konto schon gab.</summary>
    private async Task<bool> KontoUndProfilAsync(DemoPerson person, CancellationToken abbruch)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var konto = await sender.Send(new KontoSicherstellen(person.Mail!), abbruch);
        if (!konto.Neu)
        {
            return false;
        }

        var profil = await sender.Send(new ProfilSicherstellen(konto.PersonId), abbruch);
        var version = profil.Version;
        foreach (var (feld, wert) in new[] { (ProfilFeld.Vorname, person.Vorname), (ProfilFeld.Nachname, person.Nachname) })
        {
            var ergebnis = await sender.Send(new ProfilFeldAendern(konto.PersonId, feld, version, null, wert), abbruch);
            version = ergebnis is ProfilAenderung.Gespeichert gespeichert
                ? gespeichert.Person.Version
                : throw new InvalidOperationException($"Demo-Profil: {feld} ließ sich nicht setzen.");
        }

        return true;
    }

    /// <summary>Die Demo-Organisation mit Andrea als Org-Admin und einer offenen Einladung; nichts, wenn es sie schon gibt.</summary>
    private async Task OrganisationAsync(CancellationToken abbruch)
    {
        await using var scope = scopes.CreateAsyncScope();
        var dienste = scope.ServiceProvider;
        var name = DemoKatalog.Organisation.Name;
        if (await dienste.GetRequiredService<OrganisationenLeseDbContext>().Organisationen.AnyAsync(o => o.Name == name, abbruch))
        {
            return;
        }

        var sender = dienste.GetRequiredService<ISender>();
        var betreiber = DemoKatalog.Personen.Single(p => p.Rolle == DemoRolle.GlobalAdmin);
        var orgAdmin = DemoKatalog.Personen.Single(p => p.Rolle == DemoRolle.OrgAdmin);
        var gerd = await sender.Send(new KontoSicherstellen(betreiber.Mail!), abbruch);
        var andrea = await sender.Send(new KontoSicherstellen(orgAdmin.Mail!), abbruch);

        var einladung = await sender.Send(new OrganisationAnlegenCommand(name, orgAdmin.Mail, gerd.PersonId), abbruch);
        _ = await sender.Send(new EinladungAnnehmenCommand(einladung.Token, andrea.KontoId, andrea.PersonId), abbruch)
            ?? throw new InvalidOperationException("Demo-Organisation: Die Einladung ließ sich nicht annehmen.");

        dienste.GetRequiredService<MandantenKontext>().Setzen(einladung.OrganisationId);
        await sender.Send(new EinladungAnlegenCommand(einladung.OrganisationId, DemoKatalog.UeberblickEinladung, OrgRolle.OrgUeberblick, andrea.PersonId), abbruch);
    }

    /// <summary>Die Demo-Freizeit mit Jonas und Miriam als Leitung; nichts, wenn es sie in der Demo-Organisation schon gibt.</summary>
    private async Task FreizeitAsync(CancellationToken abbruch)
    {
        await using var scope = scopes.CreateAsyncScope();
        var dienste = scope.ServiceProvider;
        var organisationId = await dienste.GetRequiredService<OrganisationenLeseDbContext>().Organisationen
            .Where(o => o.Name == DemoKatalog.Organisation.Name)
            .Select(o => o.Id)
            .SingleAsync(abbruch);
        var mandant = dienste.GetRequiredService<MandantenKontext>();
        mandant.Setzen(organisationId);
        var freizeit = DemoKatalog.Freizeit;
        if (await dienste.GetRequiredService<FreizeitenLeseDbContext>().Freizeiten.AnyAsync(f => f.Name == freizeit.Name, abbruch))
        {
            return;
        }

        var sender = dienste.GetRequiredService<ISender>();
        var orgAdmin = DemoKatalog.Personen.Single(p => p.Rolle == DemoRolle.OrgAdmin);
        var andrea = await sender.Send(new KontoSicherstellen(orgAdmin.Mail!), abbruch);
        var freizeitId = await sender.Send(new FreizeitAnlegenCommand(organisationId, freizeit.Name, freizeit.Beginn, freizeit.Ende, andrea.PersonId), abbruch);
        foreach (var leitung in DemoKatalog.Personen.Where(p => p.Rolle == DemoRolle.Leitung))
        {
            var konto = await sender.Send(new KontoSicherstellen(leitung.Mail!), abbruch);
            mandant.Setzen(organisationId);
            var einladung = await sender.Send(new LeitungEinladenCommand(organisationId, freizeitId, leitung.Mail, andrea.PersonId), abbruch);
            _ = await sender.Send(new LeitungAnnehmenCommand(einladung.Token, leitung.Mail, konto.PersonId), abbruch)
                ?? throw new InvalidOperationException("Demo-Freizeit: Die Leitungs-Einladung ließ sich nicht annehmen.");
        }
    }

    /// <summary>
    /// Die Kinder der Familie eines Demo-Elternteils per <see cref="KindAnlegen"/>; ein Kind mit gleichem Vornamen in der
    /// Familie wird übersprungen. Ein schon volljähriges Kind (Paul) entsteht wie in der Web-App: anlegen unter 18, dann das
    /// Geburtsdatum ändern – ab 18 lässt sich kein Kind anlegen, ein geändertes Geburtsdatum darf aber volljährig sein.
    /// </summary>
    private async Task KinderAsync(DemoPerson eltern, CancellationToken abbruch)
    {
        await using var scope = scopes.CreateAsyncScope();
        var dienste = scope.ServiceProvider;
        var sender = dienste.GetRequiredService<ISender>();
        var uhr = dienste.GetRequiredService<TimeProvider>();
        var personId = (await sender.Send(new KontoSicherstellen(eltern.Mail!), abbruch)).PersonId;
        var lesen = dienste.GetRequiredService<PersonenLeseDbContext>();
        var vorhanden = await lesen.Personen
            .Where(p => lesen.Elternteile.Any(e => e.PersonId == personId && e.FamilieId == p.FamilieId))
            .Select(p => p.Vorname)
            .ToListAsync(abbruch);

        foreach (var kind in DemoKatalog.Personen.Where(p => p.Rolle == DemoRolle.Kind && p.Familie == eltern.Familie && !vorhanden.Contains(p.Vorname)))
        {
            var geburtsdatum = kind.Geburtsdatum!.Value;
            var heute = Volljaehrigkeit.Heute(uhr);
            var volljaehrig = Volljaehrigkeit.Ist(geburtsdatum, heute);
            var vorlaeufig = volljaehrig ? heute.AddYears(-Volljaehrigkeit.Alter + 1) : geburtsdatum;
            var id = await sender.Send(new KindAnlegen(personId, kind.Vorname, kind.Nachname, vorlaeufig), abbruch);
            if (volljaehrig)
            {
                var ergebnis = await sender.Send(new KindFeldAendern(
                    personId, id, KindFeld.Geburtsdatum, 0, vorlaeufig.ToString(KindFelder.Datumsformat, CultureInfo.InvariantCulture),
                    geburtsdatum.ToString(KindFelder.Datumsformat, CultureInfo.InvariantCulture)), abbruch);
                _ = ergebnis as KindAenderung.Gespeichert ?? throw new InvalidOperationException("Demo-Familie: Das Geburtsdatum ließ sich nicht setzen.");
            }
        }
    }

    /// <summary>Story 1-6: „Erste Hilfe“ ist in der Demo-Organisation Pflicht; Andrea ändert die Richtlinie nur, wenn nötig.</summary>
    private async Task RichtlinieAsync(CancellationToken abbruch)
    {
        await using var scope = scopes.CreateAsyncScope();
        var dienste = scope.ServiceProvider;
        var organisationId = await dienste.GetRequiredService<OrganisationenLeseDbContext>().Organisationen
            .Where(o => o.Name == DemoKatalog.Organisation.Name)
            .Select(o => o.Id)
            .SingleAsync(abbruch);
        dienste.GetRequiredService<MandantenKontext>().Setzen(organisationId);
        var zeile = await dienste.GetRequiredService<OrganisationenLeseDbContext>().Richtlinien
            .Where(r => r.Art == NachweisArt.ErsteHilfe)
            .Select(r => new { r.Pflicht, r.Version })
            .SingleAsync(abbruch);
        if (zeile.Pflicht)
        {
            return;
        }

        var ergebnis = await dienste.GetRequiredService<ISender>().Send(
            new RichtlinieFeldAendern(organisationId, NachweisArt.ErsteHilfe, Zeltlotse.Api.Contracts.Organisationen.RichtlinieFeld.Pflicht, zeile.Version, "false", "true"), abbruch);
        if (ergebnis is not RichtlinienAenderung.Gespeichert)
        {
            throw new InvalidOperationException("Demo-Richtlinie: Erste Hilfe ließ sich nicht auf Pflicht setzen.");
        }
    }

    /// <summary>
    /// Story 1-5: Jonas' Nachweise mit kleinen erfundenen Demo-Dateien (<see cref="DemoKatalog.Nachweise"/>), die automatische
    /// Einsicht für Jonas und Miriam, Jonas gibt frei, Miriam übernimmt die Prüfung und bestätigt Erste Hilfe. Jeder Schritt nur,
    /// wenn er noch fehlt.
    /// </summary>
    private async Task NachweiseAsync(CancellationToken abbruch)
    {
        await using var scope = scopes.CreateAsyncScope();
        var dienste = scope.ServiceProvider;
        var sender = dienste.GetRequiredService<ISender>();
        var mandant = dienste.GetRequiredService<MandantenKontext>();
        var organisationId = await dienste.GetRequiredService<OrganisationenLeseDbContext>().Organisationen
            .Where(o => o.Name == DemoKatalog.Organisation.Name)
            .Select(o => o.Id)
            .SingleAsync(abbruch);
        mandant.Setzen(organisationId);
        var freizeitId = await dienste.GetRequiredService<FreizeitenLeseDbContext>().Freizeiten
            .Where(f => f.Name == DemoKatalog.Freizeit.Name)
            .Select(f => f.Id)
            .SingleAsync(abbruch);
        var leitung = DemoKatalog.Personen.Where(p => p.Rolle == DemoRolle.Leitung).ToList();
        var jonas = (await sender.Send(new KontoSicherstellen(leitung[0].Mail!), abbruch)).PersonId;
        var miriam = (await sender.Send(new KontoSicherstellen(leitung[1].Mail!), abbruch)).PersonId;
        var lesen = dienste.GetRequiredService<PersonenLeseDbContext>();

        foreach (var (art, ausstellung, datei) in DemoKatalog.Nachweise)
        {
            var vorhanden = await lesen.Nachweise.Where(n => n.PersonId == jonas && n.Art == art).Select(n => new { n.Id, n.HochgeladenAm }).SingleOrDefaultAsync(abbruch);
            var id = vorhanden?.Id
                     ?? (await sender.Send(new NachweisAnlegen(jonas, art, ausstellung), abbruch) as NachweisAnlage.Angelegt
                         ?? throw new InvalidOperationException("Demo-Nachweis: ließ sich nicht anlegen.")).NachweisId;
            if (vorhanden?.HochgeladenAm is null)
            {
                _ = await sender.Send(new NachweisDateiHochladen(jonas, id, datei()), abbruch) as NachweisDateiErgebnis.Hochgeladen
                    ?? throw new InvalidOperationException("Demo-Nachweis: Die Datei ließ sich nicht hochladen.");
            }
        }

        // Die Freizeit-Rollen erreichen Personen über die Outbox: erst zustellen (Miriam übernimmt nur mit Jonas' Rolle in der
        // Liste von Personen), dann die automatische Einsicht ausdrücklich sicherstellen (idempotent).
        await dienste.GetRequiredService<IOutboxZustellung>().AllesZustellenAsync(abbruch);
        await sender.Send(new AutomatischeEinsichtSicherstellen(organisationId, freizeitId, miriam), abbruch);
        var einsichtId = await sender.Send(new AutomatischeEinsichtSicherstellen(organisationId, freizeitId, jonas), abbruch);
        var einsicht = await lesen.Einsichten.SingleAsync(e => e.Id == einsichtId, abbruch);
        if (einsicht.Status == EinsichtStatus.Abgeschlossen)
        {
            return;
        }

        // Auch nach Tagen: Ist die Freigabe abgelaufen, gibt Jonas dieselbe Einsicht erneut frei; Entschiedenes bleibt entschieden.
        if (einsicht.Freigebbar(dienste.GetRequiredService<TimeProvider>().GetUtcNow()))
        {
            _ = await sender.Send(new EinsichtFreigeben(jonas, einsichtId), abbruch) as EinsichtFreigabe.Freigegeben
                ?? throw new InvalidOperationException("Demo-Einsicht: Jonas konnte nicht freigeben.");
        }

        if (einsicht.PruefendePersonId is null)
        {
            _ = await sender.Send(new EinsichtPruefungUebernehmen(freizeitId, miriam, einsichtId), abbruch) as PruefungUebernahme.Gebunden
                ?? throw new InvalidOperationException("Demo-Einsicht: Miriam konnte die Prüfung nicht übernehmen.");
        }

        var ersteHilfe = await lesen.Nachweise.Where(n => n.PersonId == jonas && n.Art == NachweisArt.ErsteHilfe).Select(n => n.Id).SingleAsync(abbruch);
        var offen = await lesen.EinsichtPosten.AnyAsync(
            p => p.EinsichtId == einsichtId && p.NachweisId == ersteHilfe && p.Ergebnis == PostenErgebnisArt.Offen, abbruch);
        if (offen && !await sender.Send(new NachweisEntscheiden(miriam, einsichtId, ersteHilfe, Bestaetigt: true, Hinweis: null), abbruch))
        {
            throw new InvalidOperationException("Demo-Einsicht: Miriam konnte Erste Hilfe nicht bestätigen.");
        }

        // Die Bestätigung erreicht Organisationen über die Outbox (NachweisEingesehen).
        await dienste.GetRequiredService<IOutboxZustellung>().AllesZustellenAsync(abbruch);
    }

    /// <summary>Story 1-7: Miriam hat Jonas' Nachweise eingesehen (Einsicht-Protokoll); nur, wenn es noch keinen Eintrag gibt.</summary>
    private async Task ZugriffsprotokollAsync(CancellationToken abbruch)
    {
        await using var scope = scopes.CreateAsyncScope();
        var dienste = scope.ServiceProvider;
        var sender = dienste.GetRequiredService<ISender>();
        var organisationId = await dienste.GetRequiredService<OrganisationenLeseDbContext>().Organisationen
            .Where(o => o.Name == DemoKatalog.Organisation.Name)
            .Select(o => o.Id)
            .SingleAsync(abbruch);
        dienste.GetRequiredService<MandantenKontext>().Setzen(organisationId);
        var freizeitId = await dienste.GetRequiredService<FreizeitenLeseDbContext>().Freizeiten
            .Where(f => f.Name == DemoKatalog.Freizeit.Name)
            .Select(f => f.Id)
            .SingleAsync(abbruch);
        var leitung = DemoKatalog.Personen.Where(p => p.Rolle == DemoRolle.Leitung).ToList();
        var jonas = (await sender.Send(new KontoSicherstellen(leitung[0].Mail!), abbruch)).PersonId;
        var miriam = (await sender.Send(new KontoSicherstellen(leitung[1].Mail!), abbruch)).PersonId;
        if (await dienste.GetRequiredService<PersonenLeseDbContext>().EinsichtProtokoll.AnyAsync(e => e.BetroffenePersonId == jonas && e.LesendePersonId == miriam, abbruch))
        {
            return;
        }

        await sender.Send(new EinsichtProtokollieren(jonas, miriam, organisationId, freizeitId, EinsichtZweck.NachweiseEingesehen), abbruch);
    }

    /// <summary>Story 2-1: die Herkunft der Demo-Personen, nur wenn sie im Profil noch fehlt.</summary>
    private async Task HerkunftAsync(CancellationToken abbruch)
    {
        foreach (var person in DemoKatalog.Personen.Where(p => p.Mail is not null && p.Herkunft is not null))
        {
            await using var scope = scopes.CreateAsyncScope();
            var dienste = scope.ServiceProvider;
            var sender = dienste.GetRequiredService<ISender>();
            var personId = (await sender.Send(new KontoSicherstellen(person.Mail!), abbruch)).PersonId;
            var profil = await dienste.GetRequiredService<PersonenLeseDbContext>().Personen
                .Where(p => p.Id == personId)
                .Select(p => new { p.Herkunft, p.Version })
                .SingleAsync(abbruch);
            if (profil.Herkunft is not null)
            {
                continue;
            }

            if (await sender.Send(new ProfilFeldAendern(personId, ProfilFeld.Herkunft, profil.Version, null, person.Herkunft), abbruch) is not ProfilAenderung.Gespeichert)
            {
                throw new InvalidOperationException("Demo-Profil: Die Herkunft ließ sich nicht setzen.");
            }
        }
    }

    /// <summary>
    /// Story 2-1: Ort, Beschreibung und Höchstzahl der Demo-Freizeit (je nur, wenn leer) und beide Anmeldelinks (fehlt einer, entsteht
    /// er). Ein vorhandener Link bleibt, damit Screenshots und geteilte Links stabil sind.
    /// </summary>
    private async Task FreizeitseiteAsync(CancellationToken abbruch)
    {
        await using var scope = scopes.CreateAsyncScope();
        var dienste = scope.ServiceProvider;
        var sender = dienste.GetRequiredService<ISender>();
        var organisationId = await dienste.GetRequiredService<OrganisationenLeseDbContext>().Organisationen
            .Where(o => o.Name == DemoKatalog.Organisation.Name)
            .Select(o => o.Id)
            .SingleAsync(abbruch);
        dienste.GetRequiredService<MandantenKontext>().Setzen(organisationId);
        var lesen = dienste.GetRequiredService<FreizeitenLeseDbContext>();
        var freizeit = await lesen.Freizeiten.SingleAsync(f => f.Name == DemoKatalog.Freizeit.Name, abbruch);
        var werte = new (FreizeitseiteFeld Feld, string Wert)[]
        {
            (FreizeitseiteFeld.Ort, DemoKatalog.Freizeit.Ort),
            (FreizeitseiteFeld.Beschreibung, DemoKatalog.Freizeit.Beschreibung),
            (FreizeitseiteFeld.HoechstzahlTeilnehmende, DemoKatalog.Freizeit.HoechstzahlTeilnehmende.ToString(CultureInfo.InvariantCulture)),
            (FreizeitseiteFeld.Teilnahmebedingungen, DemoKatalog.Freizeit.Teilnahmebedingungen),
            (FreizeitseiteFeld.KontaktMail, DemoKatalog.KontaktFuerAbsagen.Mail),
            (FreizeitseiteFeld.KontaktTelefon, DemoKatalog.KontaktFuerAbsagen.Telefon),
        };
        foreach (var (feld, wert) in werte.Where(w => freizeit.Wert(w.Feld) is null))
        {
            if (await sender.Send(new FreizeitseiteFeldAendernCommand(freizeit.Id, feld, freizeit.Version, null, wert), abbruch) is not FreizeitseiteAenderung.Gespeichert)
            {
                throw new InvalidOperationException($"Demo-Freizeit: {feld} ließ sich nicht setzen.");
            }
        }

        var vorhanden = await lesen.Anmeldelinks.Where(l => l.FreizeitId == freizeit.Id).Select(l => l.Art).ToListAsync(abbruch);
        foreach (var art in new[] { LinkArt.Teilnehmende, LinkArt.Mitarbeitende }.Where(a => !vorhanden.Contains(a)))
        {
            if (await sender.Send(new AnmeldelinkErneuernCommand(freizeit.Id, art), abbruch) is not AnmeldelinkErneuerung.Erneuert)
            {
                throw new InvalidOperationException("Demo-Freizeit: Der Anmeldelink ließ sich nicht erzeugen.");
            }
        }
    }

    /// <summary>
    /// Story 2-2 und 2-3: die Anmeldungen aus <see cref="DemoKatalog.Anmeldungen"/> mit Konto über den passenden Anmeldelink
    /// (Teilnahmebedingungen bestätigt), je nur, wenn die Person noch nicht angemeldet ist; danach bestätigt Miriam die vorgesehenen,
    /// solange sie noch „eingegangen“ sind. Freigaben, Team-Rolle, Mitgliedschaft und die Bestätigungs-Mails entstehen über die Outbox;
    /// sie wird gleich zugestellt. Nach dem Ende der Demo-Freizeit gilt der Anmeldelink nicht mehr; dann bleibt es ohne Anmeldung.
    /// </summary>
    private async Task AnmeldungAsync(CancellationToken abbruch)
    {
        await using var scope = scopes.CreateAsyncScope();
        var dienste = scope.ServiceProvider;
        var sender = dienste.GetRequiredService<ISender>();
        var organisationId = await dienste.GetRequiredService<OrganisationenLeseDbContext>().Organisationen
            .Where(o => o.Name == DemoKatalog.Organisation.Name)
            .Select(o => o.Id)
            .SingleAsync(abbruch);
        dienste.GetRequiredService<MandantenKontext>().Setzen(organisationId);
        var lesen = dienste.GetRequiredService<FreizeitenLeseDbContext>();
        var personen = dienste.GetRequiredService<PersonenLeseDbContext>();
        var freizeitId = await lesen.Freizeiten.Where(f => f.Name == DemoKatalog.Freizeit.Name).Select(f => f.Id).SingleAsync(abbruch);
        var schutz = dienste.GetRequiredService<Zeltlotse.SharedKernel.Zugangslinks.ILinkSchutz>();

        foreach (var demo in DemoKatalog.Anmeldungen)
        {
            var angemeldet = (await sender.Send(new KontoSicherstellen(demo.Mail), abbruch)).PersonId;
            var personId = demo.Kind is null
                ? angemeldet
                : await personen.Personen
                    .Where(p => p.Vorname == demo.Kind && personen.Elternteile.Any(e => e.PersonId == angemeldet && e.FamilieId == p.FamilieId))
                    .Select(p => p.Id)
                    .SingleAsync(abbruch);
            var anmeldung = await lesen.Anmeldungen.Where(a => a.FreizeitId == freizeitId && a.PersonId == personId).Select(a => new { a.Id, a.Status }).SingleOrDefaultAsync(abbruch);
            var anmeldungId = anmeldung?.Id;
            if (anmeldung is null)
            {
                var art = demo.Kind is null ? LinkArt.Mitarbeitende : LinkArt.Teilnehmende;
                var link = await lesen.Anmeldelinks.SingleAsync(l => l.FreizeitId == freizeitId && l.Art == art, abbruch);
                var token = AnmeldelinkErzeugen.Token(schutz, link);
                if (await sender.Send(new AnmeldenMitKontoCommand(token, angemeldet, personId, TeilnahmebedingungenBestaetigt: true), abbruch)
                    is not AnmeldungErgebnis.Angelegt angelegt)
                {
                    continue;
                }

                anmeldungId = angelegt.AnmeldungId;
            }

            if (demo.Bestaetigt && (anmeldung is null || anmeldung.Status == AnmeldeStatus.Eingegangen)
                                && await sender.Send(new AnmeldungEntscheiden(freizeitId, anmeldungId!.Value, Entscheidung.Bestaetigt), abbruch)
                                    is not EntscheidungErgebnis.Entschieden)
            {
                throw new InvalidOperationException("Demo-Anmeldung: Die Bestätigung ließ sich nicht eintragen.");
            }
        }

        await dienste.GetRequiredService<IOutboxZustellung>().AllesZustellenAsync(abbruch);
    }

    /// <summary>
    /// Story 2-4: die Fragen-Auswahl der Demo-Freizeit (<see cref="DemoKatalog.Fragen"/>, je Zielgruppe nur, wenn leer). Story 2-5: vor den
    /// Anmeldungen, damit Eingang und Bestätigung die Fragen tragen.
    /// </summary>
    private async Task FragenAuswahlAsync(CancellationToken abbruch)
    {
        await using var scope = scopes.CreateAsyncScope();
        var dienste = scope.ServiceProvider;
        var sender = dienste.GetRequiredService<ISender>();
        dienste.GetRequiredService<MandantenKontext>().Setzen(await OrganisationIdAsync(dienste, abbruch));
        var freizeit = await dienste.GetRequiredService<FreizeitenLeseDbContext>().Freizeiten.SingleAsync(f => f.Name == DemoKatalog.Freizeit.Name, abbruch);
        foreach (var (feld, wert) in new[] { (FragenFeld.Teilnehmende, DemoKatalog.Fragen.Teilnehmende), (FragenFeld.Mitarbeitende, DemoKatalog.Fragen.Mitarbeitende) })
        {
            if (freizeit.Fragen(feld) is null
                && await sender.Send(new FragenAuswahlAendernCommand(freizeit.Id, feld, null, wert), abbruch) is not FragenAenderung.Gespeichert)
            {
                throw new InvalidOperationException($"Demo-Freizeit: Die Fragen für {feld} ließen sich nicht setzen.");
            }
        }
    }

    /// <summary>
    /// Story 2-4: Emmas Antworten (<see cref="DemoKatalog.EmmasFreizeitpass"/>), von Katrin gespeichert; Story 2-5: Mias Antworten
    /// (<see cref="DemoKatalog.MiasFreizeitpass"/>, seit Story 2-11 mit Medikamenten), von Daniel gespeichert. Je Frage nur, wenn es noch
    /// keine Antwort gibt.
    /// </summary>
    private async Task FreizeitpassAsync(CancellationToken abbruch)
    {
        await using var scope = scopes.CreateAsyncScope();
        var dienste = scope.ServiceProvider;
        var sender = dienste.GetRequiredService<ISender>();
        var personen = dienste.GetRequiredService<PersonenLeseDbContext>();
        foreach (var (mail, kindName, antworten) in new[]
                 {
                     ("katrin.muster@example.org", "Emma", DemoKatalog.EmmasFreizeitpass), ("daniel.probst@example.org", "Mia", DemoKatalog.MiasFreizeitpass),
                 })
        {
            var eltern = (await sender.Send(new KontoSicherstellen(mail), abbruch)).PersonId;
            var kind = await KindAsync(personen, eltern, kindName, abbruch);
            var vorhanden = await personen.FreizeitpassAntworten.Where(a => a.PersonId == kind).Select(a => a.Kennung).ToListAsync(abbruch);
            foreach (var (kennung, wert) in antworten.Where(a => !vorhanden.Contains(a.Kennung)))
            {
                if (await sender.Send(new FreizeitpassAntwortAendern(eltern, kind, kennung, FreizeitpassFeld.Wert, null, wert), abbruch) is not AntwortAenderung.Gespeichert)
                {
                    throw new InvalidOperationException($"Demo-Freizeitpass: {kennung} ließ sich nicht speichern.");
                }
            }
        }
    }

    /// <summary>
    /// Story 2-5: Lea (Team) schreibt eine Team-Notiz zu Mias Anmeldung (<see cref="DemoKatalog.TeamNotizZuMia"/>), nur wenn es dort noch
    /// keine gibt. Ohne Anmeldung (nach dem Ende der Demo-Freizeit) bleibt es ohne Notiz.
    /// </summary>
    private async Task TeamNotizAsync(CancellationToken abbruch)
    {
        await using var scope = scopes.CreateAsyncScope();
        var dienste = scope.ServiceProvider;
        var sender = dienste.GetRequiredService<ISender>();
        dienste.GetRequiredService<MandantenKontext>().Setzen(await OrganisationIdAsync(dienste, abbruch));
        var lesen = dienste.GetRequiredService<FreizeitenLeseDbContext>();
        var freizeitId = await lesen.Freizeiten.Where(f => f.Name == DemoKatalog.Freizeit.Name).Select(f => f.Id).SingleAsync(abbruch);
        var daniel = (await sender.Send(new KontoSicherstellen("daniel.probst@example.org"), abbruch)).PersonId;
        var mia = await KindAsync(dienste.GetRequiredService<PersonenLeseDbContext>(), daniel, "Mia", abbruch);
        var anmeldungId = await lesen.Anmeldungen.Where(a => a.FreizeitId == freizeitId && a.PersonId == mia).Select(a => (Guid?)a.Id).SingleOrDefaultAsync(abbruch);
        if (anmeldungId is not { } anmeldung || await lesen.TeamNotizen.AnyAsync(n => n.AnmeldungId == anmeldung, abbruch))
        {
            return;
        }

        var lea = (await sender.Send(new KontoSicherstellen("lea.feuerstein@example.org"), abbruch)).PersonId;
        if (await sender.Send(new TeamNotizAnlegenCommand(freizeitId, anmeldung, lea, DemoKatalog.TeamNotizZuMia), abbruch) is not TeamNotizAenderung.Angelegt)
        {
            throw new InvalidOperationException("Demo-Team-Notiz: ließ sich nicht anlegen.");
        }
    }

    /// <summary>
    /// Story 2-7: Andrea lädt die Org-Dokumente hoch (<see cref="DemoKatalog.OrgDokumente"/>), Miriam die Dokumente der Freizeit
    /// (<see cref="DemoKatalog.Dokumente"/>) und bindet die Hausordnung mit Sichtbarkeit „Eltern und Teilnehmende“ ein. Je nur, wenn es den
    /// Titel bzw. die Einbindung noch nicht gibt; die Dateien sind kleine erfundene PDFs.
    /// </summary>
    private async Task DokumenteAsync(CancellationToken abbruch)
    {
        await using var scope = scopes.CreateAsyncScope();
        var dienste = scope.ServiceProvider;
        var sender = dienste.GetRequiredService<ISender>();
        var organisationId = await OrganisationIdAsync(dienste, abbruch);
        dienste.GetRequiredService<MandantenKontext>().Setzen(organisationId);
        var org = dienste.GetRequiredService<OrganisationenLeseDbContext>();
        var lesen = dienste.GetRequiredService<FreizeitenLeseDbContext>();
        var freizeitId = await lesen.Freizeiten.Where(f => f.Name == DemoKatalog.Freizeit.Name).Select(f => f.Id).SingleAsync(abbruch);

        var orgTitel = await org.Dokumente.Select(d => d.Titel).ToListAsync(abbruch);
        foreach (var dateiname in DemoKatalog.OrgDokumente.Where(n => !orgTitel.Contains(Path.GetFileNameWithoutExtension(n))))
        {
            await sender.Send(
                new OrgDokumentHochladenCommand(organisationId, dateiname, DemoDateien.Pdf($"Demo-Dokument: {Path.GetFileNameWithoutExtension(dateiname)} (erfunden)"), SharedKernel.Dateien.DateiArt.Pdf),
                abbruch);
        }

        var titel = await lesen.Dokumente.Where(d => d.FreizeitId == freizeitId && d.Titel != null).Select(d => d.Titel!).ToListAsync(abbruch);
        foreach (var (dateiname, sichtbarkeit) in DemoKatalog.Dokumente.Where(d => !titel.Contains(Path.GetFileNameWithoutExtension(d.Dateiname))))
        {
            var inhalt = DemoDateien.Pdf($"Demo-Dokument: {Path.GetFileNameWithoutExtension(dateiname)} (erfunden)");
            if (await sender.Send(new DokumentHochladenCommand(freizeitId, dateiname, sichtbarkeit, inhalt, SharedKernel.Dateien.DateiArt.Pdf), abbruch)
                is not DokumentAenderung.Gespeichert)
            {
                throw new InvalidOperationException("Demo-Dokument: ließ sich nicht hochladen.");
            }
        }

        var hausordnung = await org.Dokumente.Where(d => d.Titel == DemoKatalog.EingebundenesOrgDokument).Select(d => d.Id).SingleAsync(abbruch);
        if (await lesen.Dokumente.AnyAsync(d => d.FreizeitId == freizeitId && d.OrgDokumentId == hausordnung, abbruch))
        {
            return;
        }

        if (await sender.Send(new DokumentEinbindenCommand(freizeitId, hausordnung), abbruch) is not DokumentAenderung.Eingebunden eingebunden
            || await sender.Send(
                new DokumentFeldAendernCommand(
                    freizeitId, eingebunden.Dokument.Id, DokumentFeld.Sichtbarkeit, 0, nameof(DokumentSichtbarkeit.Team), nameof(DokumentSichtbarkeit.ElternUndTeilnehmende)),
                abbruch) is not DokumentAenderung.Gespeichert)
        {
            throw new InvalidOperationException("Demo-Dokument: Die Hausordnung ließ sich nicht einbinden.");
        }
    }

    /// <summary>
    /// Story 2-8: Miriam richtet die Sicherheitskette der Demo-Freizeit ein (<see cref="DemoKatalog.Sicherheitskette"/>), über den Command des
    /// Moduls Sicherheit und je Feld nur, wenn es noch leer ist.
    /// </summary>
    private async Task SicherheitsketteAsync(CancellationToken abbruch)
    {
        await using var scope = scopes.CreateAsyncScope();
        var dienste = scope.ServiceProvider;
        var sender = dienste.GetRequiredService<ISender>();
        dienste.GetRequiredService<MandantenKontext>().Setzen(await OrganisationIdAsync(dienste, abbruch));
        var freizeitId = await dienste.GetRequiredService<FreizeitenLeseDbContext>().Freizeiten
            .Where(f => f.Name == DemoKatalog.Freizeit.Name)
            .Select(f => f.Id)
            .SingleAsync(abbruch);
        var kette = await dienste.GetRequiredService<SicherheitLeseDbContext>().Sicherheitsketten.SingleOrDefaultAsync(k => k.FreizeitId == freizeitId, abbruch);
        var werte = new (SicherheitsketteFeld Feld, string Wert)[]
        {
            (SicherheitsketteFeld.WasTun, string.Join('\n', DemoKatalog.Sicherheitskette.WasTun)),
            (SicherheitsketteFeld.LeitungTelefon, DemoKatalog.Sicherheitskette.LeitungTelefon),
            (SicherheitsketteFeld.HeimatName, DemoKatalog.Sicherheitskette.HeimatName),
            (SicherheitsketteFeld.HeimatTelefon, DemoKatalog.Sicherheitskette.HeimatTelefon),
        };
        foreach (var (feld, wert) in werte.Where(w => kette?.Wert(w.Feld) is null))
        {
            if (await sender.Send(new SicherheitsketteFeldAendernCommand(freizeitId, feld, kette?.Version ?? 0, null, wert), abbruch)
                is not SicherheitsketteAenderung.Gespeichert)
            {
                throw new InvalidOperationException($"Demo-Sicherheitskette: {feld} ließ sich nicht setzen.");
            }
        }
    }

    /// <summary>
    /// Story 2-9: Andrea lädt das Org-Dokument <see cref="DemoKatalog.Schutzkonzept"/> hoch, die Demo-Freizeit bindet es ein (Sichtbarkeit
    /// Team) und wählt es als Schutzkonzept, Jonas bestätigt es als gelesen. Über die Commands der Module und je nur, wenn es fehlt; ein schon
    /// gewähltes anderes Schutzkonzept bleibt.
    /// </summary>
    private async Task SchutzkonzeptAsync(CancellationToken abbruch)
    {
        await using var scope = scopes.CreateAsyncScope();
        var dienste = scope.ServiceProvider;
        var sender = dienste.GetRequiredService<ISender>();
        var organisationId = await OrganisationIdAsync(dienste, abbruch);
        dienste.GetRequiredService<MandantenKontext>().Setzen(organisationId);
        var org = dienste.GetRequiredService<OrganisationenLeseDbContext>();
        var lesen = dienste.GetRequiredService<FreizeitenLeseDbContext>();
        var freizeitId = await lesen.Freizeiten.Where(f => f.Name == DemoKatalog.Freizeit.Name).Select(f => f.Id).SingleAsync(abbruch);

        if (!await org.Dokumente.AnyAsync(d => d.Titel == DemoKatalog.Schutzkonzept, abbruch))
        {
            await sender.Send(
                new OrgDokumentHochladenCommand(
                    organisationId, $"{DemoKatalog.Schutzkonzept}.pdf", DemoDateien.Pdf($"Demo-Dokument: {DemoKatalog.Schutzkonzept} (erfunden)"),
                    SharedKernel.Dateien.DateiArt.Pdf),
                abbruch);
        }

        var orgDokument = await org.Dokumente.Where(d => d.Titel == DemoKatalog.Schutzkonzept).Select(d => d.Id).SingleAsync(abbruch);
        var dokumentId = await lesen.Dokumente.Where(d => d.FreizeitId == freizeitId && d.OrgDokumentId == orgDokument).Select(d => (Guid?)d.Id).SingleOrDefaultAsync(abbruch);
        if (dokumentId is null)
        {
            if (await sender.Send(new DokumentEinbindenCommand(freizeitId, orgDokument), abbruch) is not DokumentAenderung.Eingebunden eingebunden)
            {
                throw new InvalidOperationException("Demo-Schutzkonzept: ließ sich nicht einbinden.");
            }

            dokumentId = eingebunden.Dokument.Id;
        }

        var sicherheit = dienste.GetRequiredService<SicherheitLeseDbContext>();
        var konzept = await sicherheit.Schutzkonzepte.SingleOrDefaultAsync(k => k.FreizeitId == freizeitId, abbruch);
        if (konzept?.DokumentId is null
            && await sender.Send(new SchutzkonzeptFeldAendernCommand(freizeitId, Api.Contracts.Sicherheit.SchutzkonzeptFeld.DokumentId, konzept?.Version ?? 0, null, dokumentId), abbruch)
                is not SchutzkonzeptAenderung.Gespeichert)
        {
            throw new InvalidOperationException("Demo-Schutzkonzept: ließ sich nicht wählen.");
        }

        if (konzept?.DokumentId is { } anderes && anderes != dokumentId)
        {
            return;
        }

        var jonas = (await sender.Send(new KontoSicherstellen("jonas.huettner@example.org"), abbruch)).PersonId;
        if (!await sicherheit.SchutzkonzeptBestaetigungen.AnyAsync(b => b.FreizeitId == freizeitId && b.DokumentId == dokumentId && b.PersonId == jonas, abbruch)
            && await sender.Send(new SchutzkonzeptGelesenCommand(freizeitId, dokumentId.Value, jonas), abbruch) != SchutzkonzeptBestaetigen.Bestaetigt)
        {
            throw new InvalidOperationException("Demo-Schutzkonzept: Jonas' Bestätigung ließ sich nicht speichern.");
        }
    }

    /// <summary>
    /// Story 2-10: die Einträge <see cref="DemoKatalog.Vorfaelle"/> zu Mia im Vorfall-Protokoll der Demo-Freizeit, über den Command des Moduls.
    /// Idempotent über die festen Kennungen: Eine vorhandene Kennung legt keinen zweiten Eintrag an.
    /// </summary>
    private async Task VorfaelleAsync(CancellationToken abbruch)
    {
        await using var scope = scopes.CreateAsyncScope();
        var dienste = scope.ServiceProvider;
        var sender = dienste.GetRequiredService<ISender>();
        dienste.GetRequiredService<MandantenKontext>().Setzen(await OrganisationIdAsync(dienste, abbruch));
        var freizeitId = await dienste.GetRequiredService<FreizeitenLeseDbContext>().Freizeiten
            .Where(f => f.Name == DemoKatalog.Freizeit.Name)
            .Select(f => f.Id)
            .SingleAsync(abbruch);
        var daniel = (await sender.Send(new KontoSicherstellen("daniel.probst@example.org"), abbruch)).PersonId;
        var mia = await KindAsync(dienste.GetRequiredService<PersonenLeseDbContext>(), daniel, "Mia", abbruch);
        foreach (var (id, verfasserMail, art, text) in DemoKatalog.Vorfaelle)
        {
            var verfasser = (await sender.Send(new KontoSicherstellen(verfasserMail), abbruch)).PersonId;
            if (await sender.Send(new VorfallAnlegenCommand(freizeitId, id, mia, verfasser, art, text), abbruch) != VorfallAnlegen.Angelegt)
            {
                throw new InvalidOperationException("Demo-Vorfall-Protokoll: Eintrag ließ sich nicht anlegen.");
            }
        }
    }

    /// <summary>
    /// Story 3-1: Die festen Mahlzeiten der Demo-Freizeit und die Programmpunkte aus <see cref="DemoKatalog.Programmpunkte"/>, über die Commands
    /// des Moduls Programm. Idempotent nach Titel: Einen Punkt mit diesem Titel in der Freizeit legt das Einspielen nicht noch einmal an. Wer einen
    /// Punkt übernimmt, tut das selbst (die Leitung teilt nichts zu).
    /// </summary>
    private async Task ProgrammAsync(CancellationToken abbruch)
    {
        await using var scope = scopes.CreateAsyncScope();
        var dienste = scope.ServiceProvider;
        var sender = dienste.GetRequiredService<ISender>();
        dienste.GetRequiredService<MandantenKontext>().Setzen(await OrganisationIdAsync(dienste, abbruch));
        var freizeitId = await dienste.GetRequiredService<FreizeitenLeseDbContext>().Freizeiten
            .Where(f => f.Name == DemoKatalog.Freizeit.Name)
            .Select(f => f.Id)
            .SingleAsync(abbruch);
        await sender.Send(new MahlzeitenSicherstellen(freizeitId), abbruch);
        var vorhanden = await dienste.GetRequiredService<ProgrammLeseDbContext>().Programmpunkte
            .Where(p => p.FreizeitId == freizeitId)
            .Select(p => p.Titel)
            .ToListAsync(abbruch);
        foreach (var (titel, tag, beginn, uebernommenVon) in DemoKatalog.Programmpunkte.Where(p => !vorhanden.Contains(p.Titel)))
        {
            var beginnText = Api.Contracts.Programm.ProgrammpunktFelder.UhrzeitText(beginn);
            if (await sender.Send(new ProgrammpunktAnlegenCommand(freizeitId, titel, tag, beginnText), abbruch) is not ProgrammpunktAnlegen.Angelegt angelegt)
            {
                throw new InvalidOperationException($"Demo-Programm: {titel} ließ sich nicht anlegen.");
            }

            if (uebernommenVon is null)
            {
                continue;
            }

            var person = (await sender.Send(new KontoSicherstellen(uebernommenVon), abbruch)).PersonId;
            if (await sender.Send(new ProgrammpunktUebergangCommand(freizeitId, angelegt.Punkt.Id, person, Uebergang.Uebernehmen), abbruch)
                is not ProgrammpunktUebergang.Erfolg)
            {
                throw new InvalidOperationException($"Demo-Programm: {titel} ließ sich nicht übernehmen.");
            }
        }
    }

    /// <summary>
    /// Story 3-2: Material an den Programmpunkten aus <see cref="DemoKatalog.Material"/>, über die Commands des Moduls Programm. Idempotent nach
    /// (Programmpunkt, Bezeichnung): Ein Material mit dieser Bezeichnung am Punkt legt das Einspielen nicht noch einmal an. Wer etwas mitbringt,
    /// tippt das selbst.
    /// </summary>
    private async Task MaterialAsync(CancellationToken abbruch)
    {
        await using var scope = scopes.CreateAsyncScope();
        var dienste = scope.ServiceProvider;
        var sender = dienste.GetRequiredService<ISender>();
        dienste.GetRequiredService<MandantenKontext>().Setzen(await OrganisationIdAsync(dienste, abbruch));
        var freizeitId = await dienste.GetRequiredService<FreizeitenLeseDbContext>().Freizeiten
            .Where(f => f.Name == DemoKatalog.Freizeit.Name)
            .Select(f => f.Id)
            .SingleAsync(abbruch);
        var lesen = dienste.GetRequiredService<ProgrammLeseDbContext>();
        var punkte = await lesen.Programmpunkte
            .Where(p => p.FreizeitId == freizeitId && p.Mahlzeit == null)
            .Select(p => new { p.Id, p.Titel })
            .ToListAsync(abbruch);
        var vorhanden = await lesen.Material
            .Where(m => m.FreizeitId == freizeitId)
            .Select(m => new { m.ProgrammpunktId, m.Bezeichnung })
            .ToListAsync(abbruch);
        foreach (var (titel, bezeichnung, menge, status, bringtMit) in DemoKatalog.Material)
        {
            var punkt = punkte.Single(p => p.Titel == titel).Id;
            if (vorhanden.Any(m => m.ProgrammpunktId == punkt && m.Bezeichnung == bezeichnung))
            {
                continue;
            }

            if (await sender.Send(new MaterialAnlegenCommand(freizeitId, punkt, bezeichnung, menge), abbruch) is not MaterialAnlegen.Angelegt angelegt)
            {
                throw new InvalidOperationException($"Demo-Material: {bezeichnung} ließ sich nicht anlegen.");
            }

            if (status != Api.Contracts.Programm.MaterialStatus.Gebraucht
                && await sender.Send(new MaterialStatusSetzenCommand(freizeitId, angelegt.Material.Id, status), abbruch) is not MaterialUebergang.Erfolg)
            {
                throw new InvalidOperationException($"Demo-Material: Status von {bezeichnung} ließ sich nicht setzen.");
            }

            if (bringtMit is null)
            {
                continue;
            }

            var person = (await sender.Send(new KontoSicherstellen(bringtMit), abbruch)).PersonId;
            if (await sender.Send(new MaterialMitbringenCommand(freizeitId, angelegt.Material.Id, person, Mitbringen: true), abbruch) is not MaterialUebergang.Erfolg)
            {
                throw new InvalidOperationException($"Demo-Material: {bezeichnung} ließ sich nicht mitbringen.");
            }
        }
    }

    /// <summary>Story 3-3: die zweite Freizeit mit Tim als Leitung; nichts, wenn es sie in der Demo-Organisation schon gibt.</summary>
    private async Task ZweiteFreizeitAsync(CancellationToken abbruch)
    {
        await using var scope = scopes.CreateAsyncScope();
        var dienste = scope.ServiceProvider;
        var organisationId = await OrganisationIdAsync(dienste, abbruch);
        var mandant = dienste.GetRequiredService<MandantenKontext>();
        mandant.Setzen(organisationId);
        var (name, beginn, ende) = DemoKatalog.ZweiteFreizeit;
        if (await dienste.GetRequiredService<FreizeitenLeseDbContext>().Freizeiten.AnyAsync(f => f.Name == name, abbruch))
        {
            return;
        }

        var sender = dienste.GetRequiredService<ISender>();
        var andrea = await sender.Send(new KontoSicherstellen(DemoKatalog.Personen.Single(p => p.Rolle == DemoRolle.OrgAdmin).Mail!), abbruch);
        var freizeitId = await sender.Send(new FreizeitAnlegenCommand(organisationId, name, beginn, ende, andrea.PersonId), abbruch);
        foreach (var leitung in DemoKatalog.Personen.Where(p => p.Rolle == DemoRolle.LeitungZweiteFreizeit))
        {
            var konto = await sender.Send(new KontoSicherstellen(leitung.Mail!), abbruch);
            mandant.Setzen(organisationId);
            var einladung = await sender.Send(new LeitungEinladenCommand(organisationId, freizeitId, leitung.Mail, andrea.PersonId), abbruch);
            _ = await sender.Send(new LeitungAnnehmenCommand(einladung.Token, leitung.Mail, konto.PersonId), abbruch)
                ?? throw new InvalidOperationException("Demo-Freizeit: Die Leitungs-Einladung ließ sich nicht annehmen.");
        }
    }

    /// <summary>Story 3-3: Inventar und Reservierungen der Demo-Organisation, idempotent nach Bezeichnung bzw. (Freizeit, Artikel).</summary>
    private async Task InventarAsync(CancellationToken abbruch)
    {
        await using var scope = scopes.CreateAsyncScope();
        var dienste = scope.ServiceProvider;
        var sender = dienste.GetRequiredService<ISender>();
        var organisationId = await OrganisationIdAsync(dienste, abbruch);
        dienste.GetRequiredService<MandantenKontext>().Setzen(organisationId);
        var lesen = dienste.GetRequiredService<InventarLeseDbContext>();
        var vorhanden = await lesen.Artikel.Select(a => a.Bezeichnung).ToListAsync(abbruch);
        foreach (var (bezeichnung, menge) in DemoKatalog.Inventar.Where(a => !vorhanden.Contains(a.Bezeichnung)))
        {
            await sender.Send(new ArtikelAnlegenCommand(organisationId, bezeichnung, menge), abbruch);
        }

        var artikel = await lesen.Artikel.ToDictionaryAsync(a => a.Bezeichnung, a => a.Id, abbruch);
        var namen = DemoKatalog.Reservierungen.Select(r => r.Freizeit).Distinct().ToList();
        var freizeiten = await dienste.GetRequiredService<FreizeitenLeseDbContext>().Freizeiten
            .Where(f => namen.Contains(f.Name))
            .ToDictionaryAsync(f => f.Name, f => f.Id, abbruch);
        var reserviert = await lesen.Reservierungen.Select(r => new { r.FreizeitId, r.ArtikelId }).ToListAsync(abbruch);
        var programm = dienste.GetRequiredService<ProgrammLeseDbContext>();
        foreach (var (freizeit, bezeichnung, von, bis, material) in DemoKatalog.Reservierungen)
        {
            var (freizeitId, artikelId) = (freizeiten[freizeit], artikel[bezeichnung]);
            if (reserviert.Any(r => r.FreizeitId == freizeitId && r.ArtikelId == artikelId))
            {
                continue;
            }

            Guid? materialId = null;
            if (material is { } m)
            {
                materialId = await (
                        from x in programm.Material
                        join p in programm.Programmpunkte on x.ProgrammpunktId equals p.Id
                        where x.FreizeitId == freizeitId && p.Titel == m.Programmpunkt && x.Bezeichnung == m.Bezeichnung
                        select x.Id)
                    .SingleAsync(abbruch);
            }

            if (await sender.Send(new ReservierenCommand(freizeitId, artikelId, von, bis, materialId), abbruch) is not Reservieren.Angelegt)
            {
                throw new InvalidOperationException($"Demo-Inventar: {bezeichnung} ließ sich nicht reservieren.");
            }
        }
    }

    /// <summary>Story 3-4: Schäden und Verluste der Demo-Freizeit, über den Command des Moduls Inventar; idempotent nach Bezeichnung.</summary>
    private async Task SchaedenAsync(CancellationToken abbruch)
    {
        await using var scope = scopes.CreateAsyncScope();
        var dienste = scope.ServiceProvider;
        var sender = dienste.GetRequiredService<ISender>();
        dienste.GetRequiredService<MandantenKontext>().Setzen(await OrganisationIdAsync(dienste, abbruch));
        var freizeitId = await dienste.GetRequiredService<FreizeitenLeseDbContext>().Freizeiten
            .Where(f => f.Name == DemoKatalog.Freizeit.Name)
            .Select(f => f.Id)
            .SingleAsync(abbruch);
        var vorhanden = await dienste.GetRequiredService<InventarLeseDbContext>().Schaeden
            .Where(s => s.FreizeitId == freizeitId)
            .Select(s => s.Bezeichnung)
            .ToListAsync(abbruch);
        foreach (var (art, bezeichnung) in DemoKatalog.Schaeden.Where(s => !vorhanden.Contains(s.Bezeichnung)))
        {
            await sender.Send(new SchadenEintragenCommand(freizeitId, art, bezeichnung), abbruch);
        }
    }

    /// <summary>
    /// Story 3-5: Aufgaben-Vorlagen der Demo-Organisation (wie von Andrea, idempotent nach Titel), dann die Übernahme der Vorlagen ins Zeltlager,
    /// eigene Aufgaben (idempotent nach Titel) und wer welche Aufgabe übernimmt und abhakt; alles über die Commands des Moduls Programm.
    /// </summary>
    private async Task AufgabenAsync(CancellationToken abbruch)
    {
        await using var scope = scopes.CreateAsyncScope();
        var dienste = scope.ServiceProvider;
        var sender = dienste.GetRequiredService<ISender>();
        dienste.GetRequiredService<MandantenKontext>().Setzen(await OrganisationIdAsync(dienste, abbruch));
        var lesen = dienste.GetRequiredService<ProgrammLeseDbContext>();
        var vorlagen = await lesen.AufgabenVorlagen.Select(v => v.Titel).ToListAsync(abbruch);
        foreach (var (titel, phase) in DemoKatalog.AufgabenVorlagen.Where(v => !vorlagen.Contains(v.Titel)))
        {
            await sender.Send(new VorlageAnlegenCommand(titel, phase), abbruch);
        }

        var freizeitId = await dienste.GetRequiredService<FreizeitenLeseDbContext>().Freizeiten
            .Where(f => f.Name == DemoKatalog.Freizeit.Name)
            .Select(f => f.Id)
            .SingleAsync(abbruch);
        await sender.Send(new AufgabenVorlagenUebernehmen(freizeitId), abbruch);
        var vorhanden = await lesen.Aufgaben.Where(a => a.FreizeitId == freizeitId).Select(a => a.Titel).ToListAsync(abbruch);
        var punkte = await lesen.Programmpunkte.Where(p => p.FreizeitId == freizeitId && p.Mahlzeit == null).ToDictionaryAsync(p => p.Titel, p => p.Id, abbruch);
        var uebernahmen = DemoKatalog.AufgabenUebernahmen.ToList();
        foreach (var (titel, phase, programmpunkt, uebernommenVon) in DemoKatalog.Aufgaben.Where(a => !vorhanden.Contains(a.Titel)))
        {
            Guid? punkt = programmpunkt is null ? null : punkte[programmpunkt];
            if (await sender.Send(new AufgabeAnlegenCommand(freizeitId, titel, phase, punkt), abbruch) is not AufgabeAnlegen.Angelegt)
            {
                throw new InvalidOperationException($"Demo-Aufgaben: {titel} ließ sich nicht anlegen.");
            }

            if (uebernommenVon is not null)
            {
                uebernahmen.Add((titel, uebernommenVon, false));
            }
        }

        var aufgaben = await lesen.Aufgaben.Where(a => a.FreizeitId == freizeitId).ToListAsync(abbruch);
        foreach (var (titel, mail, erledigt) in uebernahmen)
        {
            var aufgabe = aufgaben.Single(a => a.Titel == titel);
            var person = (await sender.Send(new KontoSicherstellen(mail), abbruch)).PersonId;
            if (aufgabe.Uebernommen is null
                && await sender.Send(new AufgabeUebergangCommand(freizeitId, aufgabe.Id, person, AufgabeUebergang.Uebernehmen), abbruch) is not AufgabeUebergangErgebnis.Erfolg)
            {
                throw new InvalidOperationException($"Demo-Aufgaben: {titel} ließ sich nicht übernehmen.");
            }

            if (erledigt && !aufgabe.Erledigt)
            {
                await sender.Send(new AufgabeUebergangCommand(freizeitId, aufgabe.Id, person, AufgabeUebergang.Erledigen), abbruch);
            }
        }
    }

    private static Task<Guid> OrganisationIdAsync(IServiceProvider dienste, CancellationToken abbruch) =>
        dienste.GetRequiredService<OrganisationenLeseDbContext>().Organisationen
            .Where(o => o.Name == DemoKatalog.Organisation.Name)
            .Select(o => o.Id)
            .SingleAsync(abbruch);

    private static Task<Guid> KindAsync(PersonenLeseDbContext personen, Guid eltern, string vorname, CancellationToken abbruch) =>
        personen.Personen
            .Where(p => p.Vorname == vorname && personen.Elternteile.Any(e => e.PersonId == eltern && e.FamilieId == p.FamilieId))
            .Select(p => p.Id)
            .SingleAsync(abbruch);

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo-Daten eingespielt ({AnzahlPersonen} Personen im Katalog, {NeueKonten} neue Konten).")]
    private partial void Eingespielt(int anzahlPersonen, int neueKonten);
}
