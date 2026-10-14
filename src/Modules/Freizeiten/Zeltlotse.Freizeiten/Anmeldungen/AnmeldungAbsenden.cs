using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Freizeiten.Daten;
using Zeltlotse.Freizeiten.Domaene;
using Zeltlotse.Freizeiten.Freizeitseite;
using Zeltlotse.Personen.Contracts.Anmeldung;
using Zeltlotse.Personen.Contracts.Namen;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mail;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;
using DomaeneAnmeldung = Zeltlotse.Freizeiten.Domaene.Anmeldung;

namespace Zeltlotse.Freizeiten.Anmeldungen;

/// <summary>Ergebnis der beiden Anmelde-Commands.</summary>
internal abstract record AnmeldungErgebnis
{
    private AnmeldungErgebnis()
    {
    }

    /// <param name="GastLinkGueltigBis">Nur bei Gast-Anmeldungen: bis wann der Gast-Link gilt, sobald die Freigabe besteht.</param>
    /// <param name="Mail">Nur bei Gast-Anmeldungen: die Mail-Adresse des Elternteils für die Eingangsmail.</param>
    public sealed record Angelegt(Guid AnmeldungId, Guid PersonId, string FreizeitName, DateTimeOffset GastLinkGueltigBis, string? Mail) : AnmeldungErgebnis
    {
        /// <summary>Keine Mail-Adresse ausgeben (AD-22).</summary>
        public override string ToString() => $"{nameof(Angelegt)}({AnmeldungId})";
    }

    /// <summary>Anmeldelink oder Gast-Token ungültig, Person nicht vertretbar: <c>404</c>.</summary>
    public sealed record NichtGefunden : AnmeldungErgebnis;

    /// <summary>Die Person ist für diese Freizeit schon angemeldet: <c>409</c> mit Code <c>konflikt</c>.</summary>
    public sealed record SchonAngemeldet : AnmeldungErgebnis;

    /// <summary>
    /// Story 3-8: Die Freizeit ist gelaufen (AD-17): <c>409</c> mit Code <c>freizeit-abgeschlossen</c>. Die Anmelde-Commands tragen keine
    /// Freizeit-Kennung (sie kommt aus dem Token), deshalb prüft der Handler selbst.
    /// </summary>
    public sealed record Abgeschlossen : AnmeldungErgebnis;
}

/// <summary>Die Bestätigung der Teilnahmebedingungen ist Pflicht (FR-19); Fehlerschlüssel <c>teilnahmebedingungenBestaetigt</c>.</summary>
internal static class AnmeldungRegeln
{
    public const string NichtBestaetigt = "Teilnahmebedingungen nicht bestätigt.";

    public static ValidationFailure? Bestaetigung(bool bestaetigt) =>
        bestaetigt ? null : new ValidationFailure(AnmeldungFelder.TeilnahmebedingungenBestaetigt, NichtBestaetigt);
}

/// <summary>
/// Gemeinsame Schritte beider Anmeldewege: Anmeldelink einlösen (Mandant aus dem Token, AD-6) und die Anmeldung „eingegangen“
/// anlegen, je Freizeit und Person nur einmal. Bekommt nur die Tabellen des Handlers, nicht den schreibenden DbContext (AD-27).
/// </summary>
internal static class AnmeldeSchritte
{
    public static async Task<AnmeldungErgebnis> AnlegenAsync(
        DbSet<DomaeneAnmeldung> anmeldungen, Freizeit freizeit, LinkArt art, Guid personId, TimeProvider uhr, string? mail, DateTimeOffset gastGueltigBis,
        CancellationToken abbruch)
    {
        var jetzt = uhr.GetUtcNow();
        if (freizeit.Phase(jetzt) == FreizeitPhase.Gelaufen)
        {
            return new AnmeldungErgebnis.Abgeschlossen();
        }

        if (await anmeldungen.AnyAsync(a => a.FreizeitId == freizeit.Id && a.PersonId == personId, abbruch))
        {
            return new AnmeldungErgebnis.SchonAngemeldet();
        }

        var freigabeBis = freizeit.Zeitpunkte(jetzt).FreigabeGueltigBis;
        var anmeldung = DomaeneAnmeldung.Eingehen(
            Kennung.Neu(uhr), freizeit.OrganisationId, freizeit.Id, personId, Art(art), jetzt, freigabeBis, freizeit.FragenFuer(Art(art)));
        anmeldungen.Add(anmeldung);
        return new AnmeldungErgebnis.Angelegt(
            anmeldung.Id, personId, freizeit.Name, gastGueltigBis > freigabeBis ? gastGueltigBis : freigabeBis, mail);
    }

    public static AnmeldeArt Art(LinkArt art) => art switch
    {
        LinkArt.Teilnehmende => AnmeldeArt.Teilnehmende,
        LinkArt.Mitarbeitende => AnmeldeArt.Mitarbeitende,
        _ => throw new ArgumentOutOfRangeException(nameof(art), art, null),
    };

    /// <summary>Schlüssel der Sperre je (Freizeit, Person): Gleichzeitiges Absenden derselben Anmeldung läuft nacheinander.</summary>
    public static string Sperre(Guid freizeitId, Guid personId) => $"anmeldung:{freizeitId}:{personId}";
}

/// <summary>
/// Gast-Anmeldung (FR-17, FR-19, AD-11): Anmeldelink für Teilnehmende gültig (Freizeitende), Gast-Token gültig und das Kind gehört
/// zur Gast-Familie (<see cref="IGastZugaenge"/>). Sonst <see cref="AnmeldungErgebnis.NichtGefunden"/> ohne Änderung und
/// <c>LinkEinloesungFehlgeschlagen</c> im Sicherheitsprotokoll. Der Anmeldelink bleibt mehrfach nutzbar.
/// </summary>
internal sealed record GastAnmeldenCommand(string? AnmeldelinkToken, string? GastToken, Guid KindPersonId, bool TeilnahmebedingungenBestaetigt)
    : ICommand<AnmeldungErgebnis>, IModulCommand<FreizeitenDbContext>, IBenutzerCommand
{
    /// <summary>Nie Tokens ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(GastAnmeldenCommand)}({KindPersonId})";
}

internal sealed class GastAnmeldenValidator : AbstractValidator<GastAnmeldenCommand>
{
    public GastAnmeldenValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (AnmeldungRegeln.Bestaetigung(c.TeilnahmebedingungenBestaetigt) is { } fehler)
            {
                kontext.AddFailure(fehler);
            }
        });
}

internal sealed class GastAnmeldenHandler(
    FreizeitenDbContext db, MandantenKontext mandant, IGastZugaenge gaeste, ISicherheitsprotokoll protokoll, TimeProvider uhr)
    : ICommandHandler<GastAnmeldenCommand, AnmeldungErgebnis>
{
    public async ValueTask<AnmeldungErgebnis> Handle(GastAnmeldenCommand command, CancellationToken cancellationToken)
    {
        var gefunden = await AnmeldelinkSuche.FindenAsync(db.Anmeldelinks, db.Freizeiten, mandant, command.AnmeldelinkToken, uhr.GetUtcNow(), cancellationToken);
        var gast = gefunden is { Link.Art: LinkArt.Teilnehmende } ? await gaeste.PruefenAsync(command.GastToken, cancellationToken) : null;
        if (gefunden is not { } treffer || gast is null || !gast.KindPersonIds.Contains(command.KindPersonId))
        {
            await protokoll.SchreibenAsync(
                SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen, organisationId: gefunden?.Link.OrganisationId, bezugId: gefunden?.Link.Id, abbruch: cancellationToken);
            return new AnmeldungErgebnis.NichtGefunden();
        }

        await db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({AnmeldeSchritte.Sperre(treffer.Freizeit.Id, command.KindPersonId)}, 0))", cancellationToken);
        return await AnmeldeSchritte.AnlegenAsync(
            db.Anmeldungen, treffer.Freizeit, LinkArt.Teilnehmende, command.KindPersonId, uhr, gast.Mail, gast.GueltigBis, cancellationToken);
    }
}

/// <summary>
/// Anmeldung mit Konto (FR-16, FR-18, AD-26): Anmeldelink gültig; bei Teilnehmenden die Person selbst oder ein Kind unter 18 der
/// eigenen Familie (<see cref="IVertretung"/>), bei Mitarbeitenden nur die Person selbst. Ein ungültiger Link schreibt
/// <c>LinkEinloesungFehlgeschlagen</c>; eine fremde Person ergibt <see cref="AnmeldungErgebnis.NichtGefunden"/>. Keine Mail: Der
/// Status steht in der App.
/// </summary>
internal sealed record AnmeldenMitKontoCommand(string? AnmeldelinkToken, Guid AngemeldetePersonId, Guid PersonId, bool TeilnahmebedingungenBestaetigt)
    : ICommand<AnmeldungErgebnis>, IModulCommand<FreizeitenDbContext>, IBenutzerCommand
{
    /// <summary>Nie das Token ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(AnmeldenMitKontoCommand)}({AngemeldetePersonId}, {PersonId})";
}

internal sealed class AnmeldenMitKontoValidator : AbstractValidator<AnmeldenMitKontoCommand>
{
    public AnmeldenMitKontoValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (AnmeldungRegeln.Bestaetigung(c.TeilnahmebedingungenBestaetigt) is { } fehler)
            {
                kontext.AddFailure(fehler);
            }
        });
}

internal sealed class AnmeldenMitKontoHandler(
    FreizeitenDbContext db, MandantenKontext mandant, IVertretung vertretung, ISicherheitsprotokoll protokoll, TimeProvider uhr)
    : ICommandHandler<AnmeldenMitKontoCommand, AnmeldungErgebnis>
{
    public async ValueTask<AnmeldungErgebnis> Handle(AnmeldenMitKontoCommand command, CancellationToken cancellationToken)
    {
        var gefunden = await AnmeldelinkSuche.FindenAsync(db.Anmeldelinks, db.Freizeiten, mandant, command.AnmeldelinkToken, uhr.GetUtcNow(), cancellationToken);
        if (gefunden is not { } treffer)
        {
            await protokoll.SchreibenAsync(SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen, command.AngemeldetePersonId, abbruch: cancellationToken);
            return new AnmeldungErgebnis.NichtGefunden();
        }

        var selbst = command.PersonId == command.AngemeldetePersonId;
        var erlaubt = treffer.Link.Art == LinkArt.Mitarbeitende
            ? selbst
            : selbst || await vertretung.DarfAnmeldenAsync(command.AngemeldetePersonId, command.PersonId, cancellationToken);
        if (!erlaubt)
        {
            return new AnmeldungErgebnis.NichtGefunden();
        }

        await db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({AnmeldeSchritte.Sperre(treffer.Freizeit.Id, command.PersonId)}, 0))", cancellationToken);
        return await AnmeldeSchritte.AnlegenAsync(
            db.Anmeldungen, treffer.Freizeit, treffer.Link.Art, command.PersonId, uhr, mail: null, DateTimeOffset.MinValue, cancellationToken);
    }
}

/// <summary>Antworten der Anmelde-Endpunkte.</summary>
internal static class AnmeldungAntworten
{
    public static ProblemHttpResult SchonAngemeldet() => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Die Person ist für diese Freizeit schon angemeldet.",
        extensions: new Dictionary<string, object?> { [Problemcodes.Feld] = Problemcodes.Konflikt });

    /// <summary>Story 3-8: Die Freizeit ist gelaufen.</summary>
    public static ProblemHttpResult Abgeschlossen() => FreizeitseiteProbleme.Abgeschlossen();
}

/// <summary>
/// <c>POST /api/freizeiten/oeffentlich/{token}/anmeldungen</c> (anonym, zugriffsbegrenzt): <c>201 {anmeldungId}</c> und die
/// Eingangsmail mit dem Gast-Link an den Elternteil (selbst angefordert, direkt nach dem Command, weil das Token nicht in die
/// Outbox darf, AD-13); Haken fehlt <c>400</c>; Link, Gast-Token oder Kind ungültig <c>404</c>; schon angemeldet <c>409 konflikt</c>;
/// Freizeit gelaufen <c>409 freizeit-abgeschlossen</c> (Story 3-8).
/// </summary>
internal static class GastAnmeldenEndpunkt
{
    public static async Task<Results<Created<AnmeldungAngelegt>, NotFound, ProblemHttpResult>> Absenden(
        string token, GastAnmeldungAbsenden anfrage, ISender sender, IPersonenNamen namen, IMailVersand mail, IConfiguration konfiguration,
        CancellationToken abbruch)
    {
        var ergebnis = await sender.Send(
            new GastAnmeldenCommand(token, anfrage.GastToken, anfrage.KindPersonId, anfrage.TeilnahmebedingungenBestaetigt), abbruch);
        switch (ergebnis)
        {
            case AnmeldungErgebnis.Angelegt angelegt:
                var vorname = (await namen.NamenAsync([angelegt.PersonId], abbruch)).GetValueOrDefault(angelegt.PersonId)?.Vorname;
                // Nach dem Commit ohne Abbruch: Die Anmeldung steht; die Mail soll auch bei einem Abbruch des Clients rausgehen.
                await EingangsMail.VerschickenAsync(
                    mail, konfiguration, angelegt.Mail!, angelegt.FreizeitName, vorname, anfrage.GastToken!, angelegt.GastLinkGueltigBis, CancellationToken.None);
                return TypedResults.Created((string?)null, new AnmeldungAngelegt(angelegt.AnmeldungId));
            case AnmeldungErgebnis.SchonAngemeldet:
                return AnmeldungAntworten.SchonAngemeldet();
            case AnmeldungErgebnis.Abgeschlossen:
                return AnmeldungAntworten.Abgeschlossen();
            default:
                return TypedResults.NotFound();
        }
    }
}

/// <summary>
/// <c>POST /api/ich/anmeldungen</c> (AD-26): <c>201 {anmeldungId}</c>, keine Mail; <c>personId</c> leer heißt die angemeldete
/// Person selbst. Haken fehlt <c>400</c>; Link ungültig oder fremde Person <c>404</c>; schon angemeldet <c>409 konflikt</c>; Freizeit gelaufen
/// <c>409 freizeit-abgeschlossen</c> (Story 3-8).
/// </summary>
internal static class AnmeldenMitKontoEndpunkt
{
    public static async Task<Results<Created<AnmeldungAngelegt>, NotFound, ProblemHttpResult>> Absenden(
        AnmeldungAbsenden anfrage, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch)
    {
        var personId = angemeldet.PersonId ?? throw new InvalidOperationException("Ohne Sitzung erreicht niemand diesen Endpunkt.");
        var ergebnis = await sender.Send(
            new AnmeldenMitKontoCommand(anfrage.AnmeldelinkToken, personId, anfrage.PersonId ?? personId, anfrage.TeilnahmebedingungenBestaetigt), abbruch);
        return ergebnis switch
        {
            AnmeldungErgebnis.Angelegt angelegt => TypedResults.Created((string?)null, new AnmeldungAngelegt(angelegt.AnmeldungId)),
            AnmeldungErgebnis.SchonAngemeldet => AnmeldungAntworten.SchonAngemeldet(),
            AnmeldungErgebnis.Abgeschlossen => AnmeldungAntworten.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
    }
}
