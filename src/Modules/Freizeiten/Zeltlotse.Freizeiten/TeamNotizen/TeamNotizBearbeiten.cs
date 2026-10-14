using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Freizeiten.Anmeldungen.Entscheiden;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Freizeiten.Daten;
using Zeltlotse.Freizeiten.Domaene;
using Zeltlotse.Freizeiten.Freizeitseite;
using Zeltlotse.Personen.Contracts.Namen;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Datenschutz;
using ApiTeamNotiz = Zeltlotse.Api.Contracts.Freizeiten.TeamNotiz;

namespace Zeltlotse.Freizeiten.TeamNotizen;

/// <summary>
/// Legt eine Team-Notiz zu einer Anmeldung dieser Freizeit an (Story 2-5, FR-26; Leitung und Team). Pflicht ist nur der Text, höchstens
/// <see cref="TeamNotizFelder.TextMaximaleLaenge"/> Zeichen; gespeichert wird er nur verschlüsselt mit dem Schlüssel der betroffenen Person
/// (AD-14). Eine Anmeldung einer anderen Freizeit, eine abgesagte oder die einer gelöschten Person ergibt
/// <see cref="TeamNotizAenderung.NichtGefunden"/> (<c>404</c>). Eine abgeschlossene Freizeit ist nur lesbar (AD-17):
/// <see cref="TeamNotizAenderung.Abgeschlossen"/> (<c>409</c> mit Code <c>freizeit-abgeschlossen</c>).
/// </summary>
internal sealed record TeamNotizAnlegenCommand(Guid FreizeitId, Guid AnmeldungId, Guid VerfasstVon, string? Text)
    : ICommand<TeamNotizAenderung>, IModulCommand<FreizeitenDbContext>, IBenutzerCommand, IFreizeitCommand
{
    /// <summary>Keinen Notiztext ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(TeamNotizAnlegenCommand)}({FreizeitId}, {AnmeldungId})";
}

internal sealed class TeamNotizAnlegenValidator : AbstractValidator<TeamNotizAnlegenCommand>
{
    public TeamNotizAnlegenValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            var text = c.Text?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                kontext.AddFailure(new ValidationFailure(TeamNotizFelder.Text, "Text fehlt."));
            }
            else if (text.Length > TeamNotizFelder.TextMaximaleLaenge)
            {
                kontext.AddFailure(new ValidationFailure(TeamNotizFelder.Text, $"Höchstens {TeamNotizFelder.TextMaximaleLaenge} Zeichen."));
            }
        });
}

internal sealed class TeamNotizAnlegenHandler(FreizeitenDbContext db, IVerschluesselung verschluesselung, TimeProvider uhr)
    : ICommandHandler<TeamNotizAnlegenCommand, TeamNotizAenderung>
{
    public async ValueTask<TeamNotizAenderung> Handle(TeamNotizAnlegenCommand command, CancellationToken cancellationToken)
    {
        var anmeldung = await db.Anmeldungen.AsNoTracking()
            .Where(a => a.Id == command.AnmeldungId && a.FreizeitId == command.FreizeitId)
            .Select(a => new { a.OrganisationId, a.PersonId, a.Status })
            .SingleOrDefaultAsync(cancellationToken);
        if (anmeldung is null || anmeldung.PersonId == GeloeschtePerson.Id || anmeldung.Status == AnmeldeStatus.Abgesagt)
        {
            return new TeamNotizAenderung.NichtGefunden();
        }

        if (await TeamNotizRegeln.AbgeschlossenAsync(db.Freizeiten, command.FreizeitId, uhr, cancellationToken))
        {
            return new TeamNotizAenderung.Abgeschlossen();
        }

        Geschuetzt text;
        try
        {
            text = await verschluesselung.VerschluesselnAsync(anmeldung.PersonId, command.Text!.Trim(), cancellationToken);
        }
        catch (PersonenSchluesselFehltAusnahme)
        {
            // Die Person ist gelöscht, ihr Platzhalter an der Anmeldung steht nur noch nicht da.
            return new TeamNotizAenderung.NichtGefunden();
        }

        var notiz = TeamNotiz.Anlegen(
            Kennung.Neu(uhr), anmeldung.OrganisationId, command.FreizeitId, command.AnmeldungId, anmeldung.PersonId, command.VerfasstVon, text, uhr.GetUtcNow());
        db.TeamNotizen.Add(notiz);
        return new TeamNotizAenderung.Angelegt(notiz.Id);
    }
}

/// <summary>Ergebnis von Anlegen und Löschen einer Team-Notiz.</summary>
internal abstract record TeamNotizAenderung
{
    private TeamNotizAenderung()
    {
    }

    /// <summary>Angelegt: <c>201</c> mit der Kennung.</summary>
    public sealed record Angelegt(Guid Id) : TeamNotizAenderung;

    /// <summary>Gelöscht: <c>204</c>.</summary>
    public sealed record Geloescht : TeamNotizAenderung;

    /// <summary>Anmeldung oder Notiz gibt es hier nicht (oder die Anmeldung ist abgesagt): <c>404</c>.</summary>
    public sealed record NichtGefunden : TeamNotizAenderung;

    /// <summary>Die Freizeit ist abgeschlossen und nur lesbar: <c>409</c> mit Code <c>freizeit-abgeschlossen</c>.</summary>
    public sealed record Abgeschlossen : TeamNotizAenderung;
}

/// <summary>Gemeinsame Prüfung beider Handler: Nach dem Abschluss ist die Freizeit nur lesbar (AD-17, wie bei der Fragen-Auswahl).</summary>
internal static class TeamNotizRegeln
{
    public static async Task<bool> AbgeschlossenAsync(IQueryable<Freizeit> freizeiten, Guid freizeitId, TimeProvider uhr, CancellationToken abbruch)
    {
        var freizeit = await freizeiten.AsNoTracking().SingleOrDefaultAsync(f => f.Id == freizeitId, abbruch);
        return freizeit?.Phase(uhr.GetUtcNow()) == FreizeitPhase.Gelaufen;
    }
}

/// <summary>
/// Löscht eine Team-Notiz (Story 2-5; jedes Mitglied von Leitung und Team, Inline-Bestätigung in der Oberfläche).
/// <see cref="TeamNotizAenderung.NichtGefunden"/>, wenn es sie an dieser Anmeldung dieser Freizeit nicht gibt (<c>404</c>); eine
/// abgeschlossene Freizeit ist nur lesbar (<see cref="TeamNotizAenderung.Abgeschlossen"/>, AD-17).
/// </summary>
internal sealed record TeamNotizLoeschenCommand(Guid FreizeitId, Guid AnmeldungId, Guid NotizId)
    : ICommand<TeamNotizAenderung>, IModulCommand<FreizeitenDbContext>, IBenutzerCommand, IFreizeitCommand;

internal sealed class TeamNotizLoeschenHandler(FreizeitenDbContext db, TimeProvider uhr) : ICommandHandler<TeamNotizLoeschenCommand, TeamNotizAenderung>
{
    public async ValueTask<TeamNotizAenderung> Handle(TeamNotizLoeschenCommand command, CancellationToken cancellationToken)
    {
        var notiz = await db.TeamNotizen.SingleOrDefaultAsync(
            n => n.Id == command.NotizId && n.AnmeldungId == command.AnmeldungId && n.FreizeitId == command.FreizeitId, cancellationToken);
        if (notiz is null)
        {
            return new TeamNotizAenderung.NichtGefunden();
        }

        if (await TeamNotizRegeln.AbgeschlossenAsync(db.Freizeiten, command.FreizeitId, uhr, cancellationToken))
        {
            return new TeamNotizAenderung.Abgeschlossen();
        }

        db.TeamNotizen.Remove(notiz);
        return new TeamNotizAenderung.Geloescht();
    }
}

/// <summary>
/// Die Team-Notizen einer Anmeldung (Story 2-5, FR-26; Leitung und Team) unter
/// <c>/api/freizeiten/{freizeitId}/anmeldungen/{anmeldungId}/notizen</c>: <c>GET</c> neueste zuerst mit Text, Verfasser und Datum;
/// <c>POST {text}</c> → <c>201 {id}</c>, leer oder zu lang <c>400</c> am Feld <c>text</c>; <c>DELETE …/{notizId}</c> → <c>204</c>; nach dem
/// Abschluss beide <c>409</c> mit Code <c>freizeit-abgeschlossen</c>. Eine
/// Anmeldung einer anderen Freizeit oder eine unbekannte Notiz <c>404</c>. Notiztexte stehen nie in Logs (AD-22).
/// </summary>
internal static class TeamNotizEndpunkte
{
    public static async Task<Results<Ok<List<ApiTeamNotiz>>, NotFound>> Laden(
        Guid freizeitId, Guid anmeldungId, FreizeitenLeseDbContext db, IPersonenNamen namen, IVerschluesselung verschluesselung, CancellationToken abbruch)
    {
        if (!await db.Anmeldungen.AnyAsync(a => a.Id == anmeldungId && a.FreizeitId == freizeitId, abbruch))
        {
            return TypedResults.NotFound();
        }

        var notizen = await db.TeamNotizen
            .Where(n => n.AnmeldungId == anmeldungId && n.FreizeitId == freizeitId)
            .OrderByDescending(n => n.AngelegtAm)
            .ThenByDescending(n => n.Id)
            .ToListAsync(abbruch);
        var verfasser = await namen.NamenAsync([.. notizen.Select(n => n.VerfasstVon).Where(p => p != GeloeschtePerson.Id).Distinct()], abbruch);
        var ergebnis = new List<ApiTeamNotiz>();
        foreach (var notiz in notizen)
        {
            if (await TextAsync(verschluesselung, notiz.Text, abbruch) is not { } text)
            {
                continue;
            }

            var name = verfasser.GetValueOrDefault(notiz.VerfasstVon);
            var geloescht = notiz.VerfasstVon == GeloeschtePerson.Id;
            ergebnis.Add(new ApiTeamNotiz(
                notiz.Id,
                text,
                notiz.VerfasstVon,
                geloescht ? AnmeldungenListeEndpunkt.GeloeschtePersonText : name?.Vorname,
                geloescht ? null : name?.Nachname,
                notiz.AngelegtAm));
        }

        return TypedResults.Ok(ergebnis);
    }

    public static async Task<Results<Created<TeamNotizAngelegt>, NotFound, ProblemHttpResult>> Anlegen(
        Guid freizeitId, Guid anmeldungId, TeamNotizAnlegen anfrage, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch)
    {
        var ich = angemeldet.PersonId ?? throw new InvalidOperationException("Ohne Sitzung erreicht niemand diesen Endpunkt.");
        return await sender.Send(new TeamNotizAnlegenCommand(freizeitId, anmeldungId, ich, anfrage.Text), abbruch) switch
        {
            TeamNotizAenderung.Angelegt angelegt => TypedResults.Created((string?)null, new TeamNotizAngelegt(angelegt.Id)),
            TeamNotizAenderung.Abgeschlossen => FreizeitseiteProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
    }

    public static async Task<Results<NoContent, NotFound, ProblemHttpResult>> Loeschen(Guid freizeitId, Guid anmeldungId, Guid notizId, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new TeamNotizLoeschenCommand(freizeitId, anmeldungId, notizId), abbruch) switch
        {
            TeamNotizAenderung.Geloescht => TypedResults.NoContent(),
            TeamNotizAenderung.Abgeschlossen => FreizeitseiteProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };

    /// <summary>Der Klartext; ist der Schlüssel der betroffenen Person schon vernichtet (gelöscht), fehlt die Notiz.</summary>
    public static async Task<string?> TextAsync(IVerschluesselung verschluesselung, Geschuetzt text, CancellationToken abbruch)
    {
        try
        {
            return await verschluesselung.EntschluesselnAsync(text, abbruch);
        }
        catch (PersonenSchluesselFehltAusnahme)
        {
            return null;
        }
    }
}
