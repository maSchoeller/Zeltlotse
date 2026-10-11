using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Freizeiten.Daten;
using Zeltlotse.Freizeiten.Leitung;
using Zeltlotse.Freizeiten.Verwaltung;
using Zeltlotse.Konten.Anmelden;
using Zeltlotse.Organisationen.Daten;
using Zeltlotse.Organisationen.Einladungen;
using Zeltlotse.Organisationen.Verwaltung;
using Zeltlotse.Personen.Profil;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Rechte;

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
/// Vor- und Nachnamen; eine vorhandene Mail-Adresse wird übersprungen. Kinder ohne Mail bekommen hier nichts (Familie: 1-4).
/// Story 1-2: Fehlt die Demo-Organisation, legt Gerd (Betreiber) sie an und lädt Andrea ein; Andrea nimmt an (Org-Admin) und
/// lädt <see cref="DemoKatalog.UeberblickEinladung"/> als Org-Überblick ein (bleibt offen). Gerd wird Global Admin erst im
/// Abgleich danach (<see cref="Start.GlobalAdmins"/>).
/// Story 1-3: Fehlt in der Demo-Organisation die Freizeit <see cref="DemoKatalog.Freizeit"/>, legt Andrea sie an und lädt
/// die Personen mit <see cref="DemoRolle.Leitung"/> ein; jede nimmt ihre Einladung per Command an. Die Ereignisse stellt der
/// normale Zustelldienst zu.
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo-Daten eingespielt ({AnzahlPersonen} Personen im Katalog, {NeueKonten} neue Konten).")]
    private partial void Eingespielt(int anzahlPersonen, int neueKonten);
}
