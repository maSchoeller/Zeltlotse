using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Programm;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Programm.Daten;
using Zeltlotse.Programm.Programme;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;
using Aufgabe = Zeltlotse.Programm.Aufgaben.Aufgabe;
using Material = Zeltlotse.Programm.Materialien.Material;
using Programmpunkt = Zeltlotse.Programm.Programme.Programmpunkt;

namespace Zeltlotse.Programm.Bibliothek;

/// <summary>
/// Story 3-6, CAP-24: Übernimmt einen Eintrag der Programmbibliothek in die Freizeit (Leitung und Team): ein neuer Programmpunkt mit dem Titel des
/// Eintrags am gewählten Tag (Uhrzeit freiwillig), je Material ein neues Material im Status „gebraucht“ (niemand bringt es mit, ohne Reservierung)
/// und je Aufgabe eine neue, freie und offene Aufgabe am Punkt. Der neue Punkt ist nicht mit dem Eintrag verknüpft, damit eine verbesserte Fassung
/// erneut abgelegt werden kann. Eine abgeschlossene Freizeit ist nur lesbar (AD-17).
/// </summary>
internal sealed record BibliothekUebernehmenCommand(Guid FreizeitId, Guid EintragId, DateOnly? Tag, string? Beginn)
    : ICommand<BibliothekUebernehmen>, IModulCommand<ProgrammDbContext>, IBenutzerCommand;

/// <summary>Ergebnis von <see cref="BibliothekUebernehmenCommand"/>; bei <see cref="Uebernommen"/> der neue Punkt (nach dem Speichern mit Version).</summary>
internal abstract record BibliothekUebernehmen
{
    private BibliothekUebernehmen()
    {
    }

    public sealed record Uebernommen(Programmpunkt Punkt) : BibliothekUebernehmen;

    /// <summary>Der Tag gehört nicht zur Freizeit.</summary>
    public sealed record TagAusserhalb : BibliothekUebernehmen;

    public sealed record Abgeschlossen : BibliothekUebernehmen;

    public sealed record NichtGefunden : BibliothekUebernehmen;
}

/// <summary>Formprüfung wie beim Anlegen eines Programmpunkts: Tag Pflicht, Uhrzeit leer oder <c>HH:mm</c>.</summary>
internal sealed class BibliothekUebernehmenValidator : AbstractValidator<BibliothekUebernehmenCommand>
{
    public BibliothekUebernehmenValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (c.Tag is null)
            {
                kontext.AddFailure(new ValidationFailure(ProgrammpunktFelder.Tag, "Tag fehlt."));
            }

            if (!ProgrammpunktFelder.UhrzeitLesen(c.Beginn, out _))
            {
                kontext.AddFailure(new ValidationFailure(ProgrammpunktFelder.Beginn, ProgrammpunktTexte.KeineUhrzeit));
            }
        });
}

internal sealed class BibliothekUebernehmenHandler(
    ProgrammDbContext db, IMandantenKontext mandant, IFreizeitZeitpunkte zeitpunkte, IFreizeitTageLesen tageLesen, TimeProvider uhr)
    : ICommandHandler<BibliothekUebernehmenCommand, BibliothekUebernehmen>
{
    public async ValueTask<BibliothekUebernehmen> Handle(BibliothekUebernehmenCommand command, CancellationToken cancellationToken)
    {
        if (mandant.OrganisationId is not { } organisationId
            || !(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit)
            || !(await tageLesen.TageAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var tage))
        {
            return new BibliothekUebernehmen.NichtGefunden();
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return new BibliothekUebernehmen.Abgeschlossen();
        }

        // Der Mandantenfilter lässt nur Einträge der eigenen Organisation durch.
        var eintrag = await db.Bibliothek.FirstOrDefaultAsync(e => e.Id == command.EintragId, cancellationToken);
        if (eintrag is null)
        {
            return new BibliothekUebernehmen.NichtGefunden();
        }

        if (!tage.Enthaelt(command.Tag!.Value))
        {
            return new BibliothekUebernehmen.TagAusserhalb();
        }

        ProgrammpunktFelder.UhrzeitLesen(command.Beginn, out var beginn);
        var punkt = Programmpunkt.Anlegen(Neu(), organisationId, command.FreizeitId, command.Tag.Value, beginn, eintrag.Titel);
        db.Programmpunkte.Add(punkt);
        foreach (var material in eintrag.Material)
        {
            db.Material.Add(Material.Anlegen(Neu(), organisationId, command.FreizeitId, punkt.Id, material.Bezeichnung, material.Menge));
        }

        foreach (var aufgabe in eintrag.Aufgaben)
        {
            db.Aufgaben.Add(Aufgabe.Anlegen(Neu(), organisationId, command.FreizeitId, aufgabe.Titel, aufgabe.Phase, punkt.Id));
        }

        return new BibliothekUebernehmen.Uebernommen(punkt);
    }

    private Guid Neu() => Guid.CreateVersion7(uhr.GetUtcNow());
}

/// <summary>
/// <c>POST /api/freizeiten/{freizeitId}/programm/bibliothek/{eintragId}/uebernehmen</c> (Leitung und Team, FR-41): Körper
/// <see cref="BibliotheksUebernahme"/>. <c>201</c> mit dem neuen Programmpunkt (<c>Location</c> ist der Wochenraster); Tag fehlt oder außerhalb der
/// Freizeit, Beginn nicht <c>HH:mm</c> <c>400</c> am Feld; unbekannter Eintrag oder Eintrag einer anderen Organisation <c>404</c>; abgeschlossene
/// Freizeit <c>409 freizeit-abgeschlossen</c>.
/// </summary>
internal static class BibliothekUebernehmenEndpunkt
{
    public static async Task<IResult> Uebernehmen(
        Guid freizeitId, Guid eintragId, BibliotheksUebernahme koerper, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new BibliothekUebernehmenCommand(freizeitId, eintragId, koerper.Tag, koerper.Beginn), abbruch) switch
        {
            BibliothekUebernehmen.Uebernommen uebernommen => TypedResults.Created(
                $"/api/freizeiten/{freizeitId}/programm", ProgrammLesen.AlsApi(uebernommen.Punkt, angemeldet.PersonId)),
            BibliothekUebernehmen.TagAusserhalb => throw new ValidierungsAusnahme(
                new Dictionary<string, string[]> { [ProgrammpunktFelder.Tag] = [ProgrammpunktAnlegenEndpunkt.TagAusserhalb] }),
            BibliothekUebernehmen.Abgeschlossen => ProgrammProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
}
