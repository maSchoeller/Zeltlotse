using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Inventar;
using Zeltlotse.Freizeiten.Contracts.Namen;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Inventar.Daten;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Inventar.Reservierungen;

/// <summary>
/// Story 3-3: Reserviert einen Artikel für die Freizeit (Leitung und Team, FR-37); ohne Von und Bis für den ganzen Zeitraum der Freizeit,
/// optional für ein Material am Programmpunkt. Eine Überschneidung mit einer anderen Freizeit hält nichts auf, sie steht nur als Konflikthinweis in
/// der Antwort. Eine abgeschlossene Freizeit ist nur lesbar (AD-17).
/// </summary>
internal sealed record ReservierenCommand(Guid FreizeitId, Guid ArtikelId, DateOnly? Von, DateOnly? Bis, Guid? MaterialId)
    : ICommand<Reservieren>, IModulCommand<InventarDbContext>, IBenutzerCommand;

/// <summary>Ergebnis von <see cref="ReservierenCommand"/>; bei <see cref="Angelegt"/> die neue Reservierung.</summary>
internal abstract record Reservieren
{
    private Reservieren()
    {
    }

    public sealed record Angelegt(Reservierung Reservierung) : Reservieren;

    public sealed record Abgeschlossen : Reservieren;

    public sealed record NichtGefunden : Reservieren;
}

internal sealed class ReservierenValidator : AbstractValidator<ReservierenCommand>
{
    public const string ZeitraumFehlt = "Von und Bis gehören zusammen: beide oder keins.";

    public const string ZeitraumFalsch = "Bis liegt vor Von.";

    public ReservierenValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (c.Von is null != c.Bis is null)
            {
                kontext.AddFailure(new ValidationFailure(InventarFelder.Bis, ZeitraumFehlt));
            }
            else if (c.Von > c.Bis)
            {
                kontext.AddFailure(new ValidationFailure(InventarFelder.Bis, ZeitraumFalsch));
            }
        });
}

internal sealed class ReservierenHandler(
    InventarDbContext db, IMandantenKontext mandant, IFreizeitZeitpunkte zeitpunkte, IFreizeitTageLesen tage, TimeProvider uhr)
    : ICommandHandler<ReservierenCommand, Reservieren>
{
    public async ValueTask<Reservieren> Handle(ReservierenCommand command, CancellationToken cancellationToken)
    {
        if (mandant.OrganisationId is not { } organisationId
            || !(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var zeitpunkt)
            || !(await tage.TageAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return new Reservieren.NichtGefunden();
        }

        if (zeitpunkt.Phase == FreizeitPhase.Gelaufen)
        {
            return new Reservieren.Abgeschlossen();
        }

        // Zeilensperre am Artikel: ein gleichzeitiges Löschen wartet und sieht danach die Reservierung.
        var artikel = await db.ArtikelSperrenAsync(command.ArtikelId, cancellationToken);
        if (artikel is null)
        {
            return new Reservieren.NichtGefunden();
        }

        var reservierung = Reservierung.Anlegen(
            Guid.CreateVersion7(uhr.GetUtcNow()),
            organisationId,
            artikel.Id,
            command.FreizeitId,
            command.Von ?? freizeit.ErsterTag,
            command.Bis ?? freizeit.LetzterTag,
            command.MaterialId);
        db.Reservierungen.Add(reservierung);
        return new Reservieren.Angelegt(reservierung);
    }
}

/// <summary>
/// <c>POST /api/freizeiten/{freizeitId}/inventar/{artikelId}/reservierungen</c> (Leitung und Team, FR-37): Körper <see cref="ReservierungNeu"/>.
/// <c>201</c> mit der Reservierung und ihren Konflikthinweisen (<c>Location</c> ist das Inventar der Freizeit); nur ein Datum oder Von nach Bis
/// <c>400</c> am Feld <c>bis</c>; Artikel unbekannt oder aus einer anderen Organisation <c>404</c>; abgeschlossene Freizeit
/// <c>409 freizeit-abgeschlossen</c>.
/// </summary>
internal static class ReservierenEndpunkt
{
    public static async Task<IResult> Anlegen(
        Guid freizeitId, Guid artikelId, ReservierungNeu koerper, ISender sender, InventarLeseDbContext db, IFreizeitNamenLesen namen, CancellationToken abbruch)
    {
        switch (await sender.Send(new ReservierenCommand(freizeitId, artikelId, koerper.Von, koerper.Bis, koerper.MaterialId), abbruch))
        {
            case Reservieren.Angelegt angelegt:
                // Gespeichert ist die Reservierung schon (die Pipeline schließt die Transaktion nach dem Handler); die Konflikte liest der Lese-Kontext.
                var konflikte = await Konflikte.LadenAsync(db, namen, [artikelId], abbruch);
                var gespeichert = await db.Reservierungen.SingleAsync(r => r.Id == angelegt.Reservierung.Id, abbruch);
                return TypedResults.Created($"/api/freizeiten/{freizeitId}/inventar", konflikte.AlsApi(gespeichert));
            case Reservieren.Abgeschlossen:
                return InventarProbleme.Abgeschlossen();
            default:
                return TypedResults.NotFound();
        }
    }
}
