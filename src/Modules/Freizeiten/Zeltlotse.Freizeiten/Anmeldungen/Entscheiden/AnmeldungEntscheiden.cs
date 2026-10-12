using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Freizeiten.Daten;
using Zeltlotse.Freizeiten.Domaene;
using Zeltlotse.Freizeiten.Leitung;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Datenschutz;
using DomaeneAnmeldung = Zeltlotse.Freizeiten.Domaene.Anmeldung;

namespace Zeltlotse.Freizeiten.Anmeldungen.Entscheiden;

/// <summary>Ergebnis von Bestätigen und Absagen.</summary>
internal enum EntscheidungErgebnis
{
    /// <summary>Entschieden: <c>204</c>.</summary>
    Entschieden,

    /// <summary>Keine Anmeldung dieser Freizeit: <c>404</c>.</summary>
    NichtGefunden,

    /// <summary>Der Übergang ist aus dem aktuellen Status nicht erlaubt: <c>409</c> mit Code <c>konflikt</c>.</summary>
    SchonEntschieden,
}

/// <summary>
/// Bestätigen (FR-20) oder Absage eintragen (FR-21) durch die Leitung, nur für eine Anmeldung dieser Freizeit (Mandantenfilter und
/// Freizeit aus der Route). Der Status ändert sich nur über <see cref="DomaeneAnmeldung.Bestaetigen"/> bzw.
/// <see cref="DomaeneAnmeldung.Absagen"/> (AD-28); Ereignis und Mail-Auftrag schreibt die Pipeline in dieselbe Transaktion.
/// Bestätigte Mitarbeitende werden Team der Freizeit, eine Absage entzieht diese Rolle wieder; eine Leitung bleibt Leitung.
/// </summary>
internal sealed record AnmeldungEntscheiden(Guid FreizeitId, Guid AnmeldungId, Entscheidung Entscheidung)
    : ICommand<EntscheidungErgebnis>, IModulCommand<FreizeitenDbContext>, IBenutzerCommand;

internal sealed class AnmeldungEntscheidenHandler(FreizeitenDbContext db, TimeProvider uhr) : ICommandHandler<AnmeldungEntscheiden, EntscheidungErgebnis>
{
    public async ValueTask<EntscheidungErgebnis> Handle(AnmeldungEntscheiden command, CancellationToken cancellationToken)
    {
        var personId = await db.Anmeldungen.AsNoTracking()
            .Where(a => a.Id == command.AnmeldungId && a.FreizeitId == command.FreizeitId)
            .Select(a => (Guid?)a.PersonId)
            .SingleOrDefaultAsync(cancellationToken);
        if (personId is not { } person)
        {
            return EntscheidungErgebnis.NichtGefunden;
        }

        // Dieselbe Sperre wie beim Anlegen (AnmeldeSchritte.Sperre): Doppelte Klicks laufen nacheinander, der zweite sieht den neuen
        // Status und erzeugt kein zweites Ereignis. Danach erst die Anmeldung verfolgt laden.
        await db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({AnmeldeSchritte.Sperre(command.FreizeitId, person)}, 0))", cancellationToken);
        var anmeldung = await db.Anmeldungen.SingleOrDefaultAsync(a => a.Id == command.AnmeldungId && a.FreizeitId == command.FreizeitId, cancellationToken);
        if (anmeldung is null)
        {
            return EntscheidungErgebnis.NichtGefunden;
        }

        var jetzt = uhr.GetUtcNow();
        var entschieden = command.Entscheidung == Entscheidung.Bestaetigt ? anmeldung.Bestaetigen(jetzt) : anmeldung.Absagen(jetzt);
        if (!entschieden)
        {
            return EntscheidungErgebnis.SchonEntschieden;
        }

        if (anmeldung.Art == AnmeldeArt.Mitarbeitende && anmeldung.PersonId != GeloeschtePerson.Id)
        {
            // Rechte entstehen erst mit der Bestätigung (AD-11): Team der Freizeit, Personen erfährt es über FreizeitRolleGeaendert.
            var freizeit = await LeitungSperre.FreizeitAsync(db.Freizeiten, anmeldung.FreizeitId, anmeldung.OrganisationId, cancellationToken);
            await db.Rollen.Where(r => r.FreizeitId == freizeit.Id).LoadAsync(cancellationToken);
            if (command.Entscheidung == Entscheidung.Bestaetigt)
            {
                freizeit.TeamEinsetzen(anmeldung.PersonId, jetzt);
            }
            else
            {
                freizeit.TeamEntziehen(anmeldung.PersonId);
            }
        }

        return EntscheidungErgebnis.Entschieden;
    }
}

/// <summary>
/// <c>POST /api/freizeiten/{freizeitId}/anmeldungen/{anmeldungId}/bestaetigen</c> und <c>…/absagen</c> (Leitung): <c>204</c>;
/// Anmeldung einer anderen Freizeit oder unbekannt <c>404</c>; Übergang nicht erlaubt <c>409</c> mit Code <c>konflikt</c>.
/// </summary>
internal static class AnmeldungEntscheidenEndpunkt
{
    public static Task<Results<NoContent, NotFound, ProblemHttpResult>> Bestaetigen(Guid freizeitId, Guid anmeldungId, ISender sender, CancellationToken abbruch) =>
        EntscheidenAsync(new AnmeldungEntscheiden(freizeitId, anmeldungId, Entscheidung.Bestaetigt), sender, abbruch);

    public static Task<Results<NoContent, NotFound, ProblemHttpResult>> Absagen(Guid freizeitId, Guid anmeldungId, ISender sender, CancellationToken abbruch) =>
        EntscheidenAsync(new AnmeldungEntscheiden(freizeitId, anmeldungId, Entscheidung.Abgesagt), sender, abbruch);

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> EntscheidenAsync(AnmeldungEntscheiden command, ISender sender, CancellationToken abbruch) =>
        await sender.Send(command, abbruch) switch
        {
            EntscheidungErgebnis.Entschieden => TypedResults.NoContent(),
            EntscheidungErgebnis.SchonEntschieden => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Diese Anmeldung wurde schon entschieden.",
                extensions: new Dictionary<string, object?> { [Problemcodes.Feld] = Problemcodes.Konflikt }),
            _ => TypedResults.NotFound(),
        };
}
