using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Api.Contracts.Finanzen;
using Zeltlotse.Finanzen.Daten;
using Zeltlotse.Finanzen.Kalkulation;
using Zeltlotse.Freizeiten.Contracts.Team;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Finanzen.Belege;

/// <summary>
/// Story 5-2 (FR-58): Legt einen Beleg von Hand an (Leitung und Team, Modul Finanzen). Pflicht sind Kategorie und Betrag; „bezahlt von“ ist ohne Angabe
/// die angemeldete Person (<see cref="AngemeldetePersonId"/>) und muss zu Leitung oder Team der Freizeit gehören. Der Beleg beginnt als nicht erstattet und
/// ohne Datei. Eine abgeschlossene Freizeit ist nur lesbar (AD-17).
/// </summary>
internal sealed record BelegAnlegenCommand(Guid FreizeitId, Guid AngemeldetePersonId, string? Kategorie, string? Betrag, string? Bezeichnung, string? BezahltVon)
    : ICommand<BelegAenderung>, IModulCommand<FinanzenDbContext>, IBenutzerCommand, ISchalterCommand
{
    public FunktionsSchalter Schalter => FunktionsSchalter.Finanzen;

    /// <summary>Keine Personenkennungen und keine Werte ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(BelegAnlegenCommand)}({FreizeitId})";
}

internal sealed class BelegAnlegenValidator : AbstractValidator<BelegAnlegenCommand>
{
    public BelegAnlegenValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            foreach (var (feld, fehler) in new[]
                     {
                         (BelegFelder.Kategorie, BelegSchritte.KategorieFehler(c.Kategorie)),
                         (BelegFelder.Betrag, BelegSchritte.BetragFehler(c.Betrag)),
                         (BelegFelder.Bezeichnung, BelegSchritte.BezeichnungFehler(c.Bezeichnung)),
                         (BelegFelder.BezahltVon, FinanzenWerte.TextOderNull(c.BezahltVon) is not null && Beleg.PersonLesen(c.BezahltVon) is null ? BelegSchritte.ZahlerFehler : null),
                     })
            {
                if (fehler is not null)
                {
                    kontext.AddFailure(new ValidationFailure(feld, fehler));
                }
            }
        });
}

internal sealed class BelegAnlegenHandler(FinanzenDbContext db, IMandantenKontext mandant, IFreizeitZeitpunkte zeitpunkte, IFreizeitTeamLesen team, TimeProvider uhr)
    : ICommandHandler<BelegAnlegenCommand, BelegAenderung>
{
    public async ValueTask<BelegAenderung> Handle(BelegAnlegenCommand command, CancellationToken cancellationToken)
    {
        if (mandant.OrganisationId is not { } organisationId)
        {
            return new BelegAenderung.NichtGefunden();
        }

        if (await BelegSchritte.FreizeitPruefenAsync(zeitpunkte, command.FreizeitId, cancellationToken) is { } abbruch)
        {
            return abbruch;
        }

        var zahler = Beleg.PersonLesen(command.BezahltVon) ?? command.AngemeldetePersonId;
        if (!await BelegSchritte.ImTeamAsync(team, command.FreizeitId, zahler, cancellationToken))
        {
            return new BelegAenderung.FremderZahler();
        }

        var beleg = Beleg.Anlegen(
            Kennung.Neu(uhr), organisationId, command.FreizeitId, uhr.GetUtcNow(), Kostenkategorien.Lesen(command.Kategorie)!.Value,
            FinanzenWerte.BetragLesen(command.Betrag)!.Value, command.Bezeichnung, zahler);
        db.Belege.Add(beleg);
        return new BelegAenderung.Gespeichert(beleg);
    }
}

/// <summary>
/// <c>POST /api/freizeiten/{freizeitId}/finanzen/belege</c> (Leitung und Team, Modul Finanzen): Körper <see cref="BelegNeu"/>. <c>201</c> mit dem Beleg
/// (mit Version); Kategorie oder Betrag fehlt oder ungültig, Bezeichnung zu lang, „bezahlt von“ keine Person aus Leitung oder Team <c>400</c> am Feld;
/// abgeschlossene Freizeit <c>409 freizeit-abgeschlossen</c>.
/// </summary>
internal static class BelegAnlegenEndpunkt
{
    public static async Task<IResult> Anlegen(Guid freizeitId, BelegNeu koerper, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch)
    {
        if (angemeldet.PersonId is not { } person)
        {
            return TypedResults.NotFound();
        }

        var ergebnis = await sender.Send(
            new BelegAnlegenCommand(freizeitId, person, koerper.Kategorie, koerper.Betrag, koerper.Bezeichnung, koerper.BezahltVon), abbruch);
        return BelegSchritte.Antwort(ergebnis, beleg => TypedResults.Created((string?)null, BelegSchritte.AlsApi(beleg)));
    }
}
