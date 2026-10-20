using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Api.Contracts.Finanzen;
using Zeltlotse.Finanzen.Daten;
using Zeltlotse.Finanzen.Kalkulation;
using Zeltlotse.Freizeiten.Contracts.Team;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Finanzen.Belege;

/// <summary>
/// Story 5-2: Ändert genau ein Feld eines Belegs (Bezeichnung, Betrag, Kategorie oder „bezahlt von“, AD-19). Konfliktregel: Weicht der aktuelle Wert
/// desselben Felds vom mitgeschickten alten Wert ab, ist das Ergebnis <see cref="BelegAenderung.Konflikt"/>. Ein neuer Zahler muss zu Leitung oder Team
/// gehören. Eine abgeschlossene Freizeit ist nur lesbar (AD-17); der Erstattungsvermerk hat einen eigenen Command.
/// </summary>
internal sealed record BelegFeldAendernCommand(Guid FreizeitId, Guid BelegId, BelegFeld Feld, uint Version, string? AlterWert, string? NeuerWert)
    : ICommand<BelegAenderung>, IModulCommand<FinanzenDbContext>, IBenutzerCommand, ISchalterCommand
{
    public FunktionsSchalter Schalter => FunktionsSchalter.Finanzen;

    /// <summary>Keine Werte ausgeben, sie können Personenkennungen sein (AD-22).</summary>
    public override string ToString() => $"{nameof(BelegFeldAendernCommand)}({FreizeitId}, {BelegId}, {Feld})";
}

internal sealed class BelegFeldAendernValidator : AbstractValidator<BelegFeldAendernCommand>
{
    public BelegFeldAendernValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (!Enum.IsDefined(c.Feld))
            {
                kontext.AddFailure(new ValidationFailure("feld", "Unbekanntes Feld."));
                return;
            }

            var fehler = c.Feld switch
            {
                BelegFeld.Bezeichnung => BelegSchritte.BezeichnungFehler(c.NeuerWert),
                BelegFeld.Betrag => BelegSchritte.BetragFehler(c.NeuerWert),
                BelegFeld.Kategorie => BelegSchritte.KategorieFehler(c.NeuerWert),
                _ => Beleg.PersonLesen(c.NeuerWert) is null ? BelegSchritte.ZahlerFehler : null,
            };
            if (fehler is not null)
            {
                kontext.AddFailure(new ValidationFailure(BelegFelder.JsonName(c.Feld), fehler));
            }
        });
}

internal sealed class BelegFeldAendernHandler(FinanzenDbContext db, IFreizeitZeitpunkte zeitpunkte, IFreizeitTeamLesen team)
    : ICommandHandler<BelegFeldAendernCommand, BelegAenderung>
{
    public async ValueTask<BelegAenderung> Handle(BelegFeldAendernCommand command, CancellationToken cancellationToken)
    {
        if (await BelegSchritte.FreizeitPruefenAsync(zeitpunkte, command.FreizeitId, cancellationToken) is { } abbruch)
        {
            return abbruch;
        }

        var beleg = await db.BelegSperrenAsync(command.FreizeitId, command.BelegId, cancellationToken);
        if (beleg is null)
        {
            return new BelegAenderung.NichtGefunden();
        }

        // Erst die Konfliktregel (AD-19), dann die Zugehörigkeit zum Team: ein veralteter alter Wert ist immer 409.
        if (!beleg.AlterWertPasst(command.Feld, command.AlterWert))
        {
            return new BelegAenderung.Konflikt();
        }

        if (command.Feld == BelegFeld.BezahltVon && !await BelegSchritte.ImTeamAsync(team, command.FreizeitId, Beleg.PersonLesen(command.NeuerWert)!.Value, cancellationToken))
        {
            return new BelegAenderung.FremderZahler();
        }

        return beleg.FeldAendern(command.Feld, command.AlterWert, command.NeuerWert) ? new BelegAenderung.Gespeichert(beleg) : new BelegAenderung.Konflikt();
    }
}

/// <summary>
/// <c>PATCH /api/freizeiten/{freizeitId}/finanzen/belege/{belegId}</c> (Leitung und Team, Modul Finanzen, AD-19): Körper
/// <c>{ "version": n, "alterWert": …, "bezeichnung"|"betrag"|"kategorie"|"bezahltVon": neu }</c>. <c>200 { "version", "&lt;feld&gt;" }</c>; ungültiger
/// Wert oder Körper <c>400</c>; anderer Wert desselben Felds <c>409 konflikt</c>; abgeschlossene Freizeit <c>409 freizeit-abgeschlossen</c>; Beleg
/// unbekannt oder aus einer anderen Freizeit <c>404</c>.
/// </summary>
internal static class BelegFeldAendernEndpunkt
{
    public static async Task<IResult> Aendern(Guid freizeitId, Guid belegId, JsonElement koerper, ISender sender, CancellationToken abbruch)
    {
        var (version, alterWert, feld, neuerWert) = FeldPatch.Lesen<BelegFeld>(koerper, BelegFelder.JsonName);
        var ergebnis = await sender.Send(new BelegFeldAendernCommand(freizeitId, belegId, feld, version, alterWert, neuerWert), abbruch);
        return BelegSchritte.Antwort(ergebnis, beleg => TypedResults.Ok(new Dictionary<string, object?>
        {
            ["version"] = beleg.Version,
            [BelegFelder.JsonName(feld)] = beleg.Wert(feld),
        }));
    }
}
