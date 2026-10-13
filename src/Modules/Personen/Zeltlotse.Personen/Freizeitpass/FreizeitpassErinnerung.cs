using FluentValidation;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Personen.Contracts.Anmeldung;
using Zeltlotse.Personen.Daten;
using Zeltlotse.Personen.Familien;
using Zeltlotse.Personen.Nachweise;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mail;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Web;

namespace Zeltlotse.Personen.Freizeitpass;

/// <summary>
/// Mail-Auftrag „Erinnerung zum Freizeitpass“ (Story 2-6, AD-13): nur Kennungen. Empfänger, Namen und Text entstehen erst beim Versand
/// (<see cref="FreizeitpassErinnerungMailAufbau"/>), nichts davon steht in der Outbox (AD-4, AD-22).
/// </summary>
internal sealed record FreizeitpassErinnerungMail(Guid PersonId, Guid ErinnertVon) : IMailAuftrag;

/// <summary>
/// Die letzte Erinnerung der Leitung an eine Person, ihren Freizeitpass zu ergänzen (Story 2-6, FR-25, NFR-10): eine Zeile je (Freizeit, Person),
/// höchstens eine Erinnerung je Kalendertag in Europe/Berlin. Jede Erinnerung merkt die Mail über die Outbox vor; verschickt wird nur nach
/// der ausdrücklich bestätigten Aktion der Leitung (AD-13). Arbeitsdaten der Freizeit (AD-16), getrennt von den Nachweis-Erinnerungen.
/// </summary>
internal sealed class FreizeitpassErinnerung : Aggregat, IOrganisationsEigen
{
    private FreizeitpassErinnerung()
    {
    }

    public Guid OrganisationId { get; private init; }

    public Guid FreizeitId { get; private init; }

    public Guid PersonId { get; private init; }

    /// <summary>Die Leitung, die zuletzt erinnert hat.</summary>
    public Guid ErinnertVon { get; private set; }

    public DateTimeOffset ErinnertAm { get; private set; }

    /// <summary>Die erste Erinnerung derselben Freizeit an die Person; legt den Mail-Auftrag ab.</summary>
    public static FreizeitpassErinnerung Neu(Guid id, Guid organisationId, Guid freizeitId, Guid personId, Guid von, DateTimeOffset jetzt)
    {
        var erinnerung = new FreizeitpassErinnerung { Id = id, OrganisationId = organisationId, FreizeitId = freizeitId, PersonId = personId };
        erinnerung.Setzen(von, jetzt);
        return erinnerung;
    }

    /// <summary>Schon am selben Kalendertag (Europe/Berlin) erinnert?</summary>
    public bool HeuteSchonErinnert(DateTimeOffset jetzt) => NachweisRegeln.Tag(ErinnertAm) == NachweisRegeln.Tag(jetzt);

    /// <summary>Eine weitere Erinnerung; <c>false</c>, wenn heute schon erinnert wurde (dann keine Mail).</summary>
    public bool Erinnern(Guid von, DateTimeOffset jetzt)
    {
        if (HeuteSchonErinnert(jetzt))
        {
            return false;
        }

        Setzen(von, jetzt);
        return true;
    }

    private void Setzen(Guid von, DateTimeOffset jetzt)
    {
        ErinnertVon = von;
        ErinnertAm = jetzt;
        MailVormerken(new FreizeitpassErinnerungMail(PersonId, von));
    }
}

/// <summary>
/// Erinnern zum Freizeitpass (Leitung im Freizeit-Kontext, Story 2-6, FR-25): nur für Personen mit aktiver Freigabe in dieser Freizeit, nicht für
/// sich selbst. Speichert in einem Command die Erinnerung und merkt die Mail vor. Keine Einsicht, keine Prüfung, ob Angaben offen sind: Die
/// Leitung ist berechtigt, und es geht höchstens eine Mail am Tag hinaus.
/// </summary>
internal sealed record FreizeitpassErinnern(Guid OrganisationId, Guid FreizeitId, Guid AnfragendePersonId, Guid? PersonId)
    : ICommand<ErinnerungErgebnis>, IModulCommand<PersonenDbContext>, IBenutzerCommand;

internal sealed class FreizeitpassErinnernValidator : AbstractValidator<FreizeitpassErinnern>
{
    public FreizeitpassErinnernValidator()
    {
        RuleFor(c => c.PersonId).NotNull().WithMessage("Person fehlt.");
    }
}

internal sealed class FreizeitpassErinnernHandler(PersonenDbContext db, TimeProvider uhr) : ICommandHandler<FreizeitpassErinnern, ErinnerungErgebnis>
{
    public async ValueTask<ErinnerungErgebnis> Handle(FreizeitpassErinnern command, CancellationToken cancellationToken)
    {
        var personId = command.PersonId!.Value;
        var jetzt = uhr.GetUtcNow();
        if (personId == command.AnfragendePersonId || !await AktivAsync(command.FreizeitId, personId, jetzt, cancellationToken))
        {
            return new ErinnerungErgebnis.NichtGefunden();
        }

        // Gleichzeitige Erinnerungen derselben Freizeit an dieselbe Person laufen nacheinander; dahinter steht der eindeutige Index.
        await FreizeitpassErinnerungRegeln.SperrenAsync(db, command.FreizeitId, personId, cancellationToken);
        var erinnerung = await db.FreizeitpassErinnerungen.SingleOrDefaultAsync(e => e.FreizeitId == command.FreizeitId && e.PersonId == personId, cancellationToken);
        if (erinnerung is null)
        {
            db.FreizeitpassErinnerungen.Add(FreizeitpassErinnerung.Neu(
                Kennung.Neu(uhr), command.OrganisationId, command.FreizeitId, personId, command.AnfragendePersonId, jetzt));
        }
        else if (!erinnerung.Erinnern(command.AnfragendePersonId, jetzt))
        {
            return new ErinnerungErgebnis.SchonHeute();
        }

        return new ErinnerungErgebnis.Erinnert(jetzt);
    }

    /// <summary>Die jüngste Freigabe der Person in dieser Freizeit ist aktiv (dieselbe Wahl wie der Stand).</summary>
    private async Task<bool> AktivAsync(Guid freizeitId, Guid personId, DateTimeOffset jetzt, CancellationToken abbruch) =>
        await db.Freigaben
            .Where(f => f.FreizeitId == freizeitId && f.PersonId == personId)
            .OrderByDescending(f => f.AngelegtAm)
            .ThenByDescending(f => f.Id)
            .Select(f => (DateTimeOffset?)f.GueltigBis)
            .FirstOrDefaultAsync(abbruch) is { } bis && bis > jetzt;
}

/// <summary>Sperre der Erinnerungen zum Freizeitpass; eigener Präfix neben den Nachweis-Erinnerungen.</summary>
internal static class FreizeitpassErinnerungRegeln
{
    public static Task SperrenAsync(DbContext db, Guid freizeitId, Guid personId, CancellationToken abbruch) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"freizeitpass-erinnerung:" + freizeitId + ":" + personId}, 0))", abbruch);
}

/// <summary>Betreff und Text der Erinnerungs-Mail: Deutsch, ohne Anrede und ohne Du/Sie, ohne Fragen und ohne Antworten.</summary>
internal static class FreizeitpassErinnerungsMail
{
    public const string Betreff = "Erinnerung: Freizeitpass in Zeltlotse";

    /// <summary>Die Familienübersicht der Web-App; dort steht der Freizeitpass bei der Anmeldung.</summary>
    public static string Link(string oeffentlicheAdresse) => $"{oeffentlicheAdresse}/app/familie";

    public static string Text(string oeffentlicheAdresse, string? leitungVorname, string? leitungNachname, string? vorname, string? nachname) =>
        $"""
        {Name(leitungVorname, leitungNachname)} aus der Leitung einer Freizeit bittet darum, den Freizeitpass von {Name(vorname, nachname)} in Zeltlotse zu ergänzen. Mit Konto steht der Freizeitpass unter „Familie“ bei der Anmeldung:

        {Link(oeffentlicheAdresse)}

        Ohne Konto öffnet der Gast-Link aus der Mail zur Anmeldung den Freizeitpass.

        Diese Mail hat die Leitung ausgelöst.
        """;

    private static string Name(string? vorname, string? nachname)
    {
        var name = string.Join(" ", new[] { vorname, nachname }.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim()));
        return name.Length == 0 ? "Eine Person" : name;
    }
}

/// <summary>
/// Baut die Erinnerungs-Mail erst beim Versand (AD-13): Empfänger über <see cref="IAnmeldeKontakt"/> (eigenes Konto, sonst ein Elternteil mit
/// Konto, sonst der Gast-Zugang), Namen aus den Profilen. Ohne Empfänger gibt es keine Mail (<c>null</c>).
/// </summary>
internal sealed class FreizeitpassErinnerungMailAufbau(IAnmeldeKontakt kontakt, PersonenLeseDbContext db, IConfiguration konfiguration)
    : IMailAufbau<FreizeitpassErinnerungMail>
{
    public async Task<MailInhalt?> AufbauenAsync(FreizeitpassErinnerungMail auftrag, CancellationToken abbruch)
    {
        if (await kontakt.MailAsync(auftrag.PersonId, abbruch) is not { } mail)
        {
            return null;
        }

        var namen = await db.Personen
            .Where(p => p.Id == auftrag.PersonId || p.Id == auftrag.ErinnertVon)
            .ToDictionaryAsync(p => p.Id, p => (p.Vorname, p.Nachname), abbruch);
        var leitung = namen.GetValueOrDefault(auftrag.ErinnertVon);
        var person = namen.GetValueOrDefault(auftrag.PersonId);
        return new MailInhalt(
            mail,
            FreizeitpassErinnerungsMail.Betreff,
            FreizeitpassErinnerungsMail.Text(OeffentlicheAdresse.Lesen(konfiguration), leitung.Vorname, leitung.Nachname, person.Vorname, person.Nachname));
    }
}

/// <summary>
/// <c>POST /api/freizeiten/{freizeitId}/personen/freizeitpass/erinnerungen {personId}</c> (nur Leitung, Story 2-6, FR-25, AD-13):
/// <c>200 {erinnertAm}</c>; ohne aktive Freigabe in dieser Freizeit oder für sich selbst <c>404</c>; heute schon erinnert (Europe/Berlin)
/// <c>409</c>; ohne <c>personId</c> <c>400</c>. Die Mail geht über die Outbox.
/// </summary>
internal static class FreizeitpassErinnernEndpunkt
{
    public static async Task<Results<Ok<FreizeitpassErinnert>, NotFound, ProblemHttpResult>> Erinnern(
        Guid freizeitId, FreizeitpassErinnerungSenden anfrage, IMandantenKontext mandant, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch)
    {
        var organisationId = mandant.OrganisationId ?? throw new InvalidOperationException("Ohne Mandanten-Kontext erreicht niemand diesen Endpunkt.");
        return await sender.Send(new FreizeitpassErinnern(organisationId, freizeitId, FamilieAntworten.PersonId(angemeldet), anfrage.PersonId), abbruch) switch
        {
            ErinnerungErgebnis.Erinnert erinnert => TypedResults.Ok(new FreizeitpassErinnert(erinnert.ErinnertAm)),
            ErinnerungErgebnis.SchonHeute => FamilieAntworten.Konflikt("Zum Freizeitpass dieser Person wurde heute schon erinnert."),
            _ => TypedResults.NotFound(),
        };
    }
}
