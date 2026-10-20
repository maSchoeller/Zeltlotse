using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Api.Contracts.Finanzen;
using Zeltlotse.Finanzen.Daten;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Finanzen.Kalkulation;

/// <summary>
/// Story 5-1 (FR-54, FR-55): Ändert genau einen Betrag einer Kostenkategorie (Fixkosten oder Kosten pro Person und Tag, AD-19). Die Zeile entsteht beim
/// ersten Ändern. Konfliktregel wie bei den Planzahlen; eine abgeschlossene Freizeit ist nur lesbar (AD-17).
/// </summary>
internal sealed record KategorieFeldAendernCommand(Guid FreizeitId, Kostenkategorie Kategorie, KategorieFeld Feld, uint Version, string? AlterWert, string? NeuerWert)
    : ICommand<FinanzenAenderung<KategoriePosten>>, IModulCommand<FinanzenDbContext>, IBenutzerCommand, ISchalterCommand
{
    public FunktionsSchalter Schalter => FunktionsSchalter.Finanzen;
}

internal sealed class KategorieFeldAendernValidator : AbstractValidator<KategorieFeldAendernCommand>
{
    public KategorieFeldAendernValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (!Enum.IsDefined(c.Feld))
            {
                kontext.AddFailure(new ValidationFailure("feld", "Unbekanntes Feld."));
            }
            else if (FinanzenWerte.BetragFehler(c.NeuerWert) is { } fehler)
            {
                kontext.AddFailure(new ValidationFailure(KategorieFelder.JsonName(c.Feld), fehler));
            }
        });
}

internal sealed class KategorieFeldAendernHandler(FinanzenDbContext db, IMandantenKontext mandant, IFreizeitZeitpunkte zeitpunkte, TimeProvider uhr)
    : ICommandHandler<KategorieFeldAendernCommand, FinanzenAenderung<KategoriePosten>>
{
    public async ValueTask<FinanzenAenderung<KategoriePosten>> Handle(KategorieFeldAendernCommand command, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(command.Kategorie)
            || mandant.OrganisationId is not { } organisationId
            || !(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return new FinanzenAenderung<KategoriePosten>.NichtGefunden();
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return new FinanzenAenderung<KategoriePosten>.Abgeschlossen();
        }

        var posten = await db.KategorieSperrenAsync(Guid.CreateVersion7(uhr.GetUtcNow()), organisationId, command.FreizeitId, command.Kategorie, cancellationToken);
        return posten.FeldAendern(command.Feld, command.AlterWert, command.NeuerWert)
            ? new FinanzenAenderung<KategoriePosten>.Gespeichert(posten)
            : new FinanzenAenderung<KategoriePosten>.Konflikt();
    }
}

/// <summary>
/// <c>PATCH /api/freizeiten/{freizeitId}/finanzen/kalkulation/kategorien/{kategorie}</c> (Leitung und Team, Modul Finanzen, AD-19): <c>kategorie</c> ist
/// der Name einer <see cref="Kostenkategorie"/> (z. B. <c>Verpflegung</c>, Groß- und Kleinschreibung egal), sonst <c>404</c>. Körper
/// <c>{ "version": n, "alterWert": …, "fixkosten"|"proPersonUndTag": neu }</c>. <c>200 { "version", "&lt;feld&gt;" }</c> mit dem normalisierten Betrag
/// (<c>"7,50"</c> → <c>"7,5"</c>); ungültiger Betrag oder Körper <c>400</c>; anderer Wert desselben Felds <c>409 konflikt</c>; abgeschlossene Freizeit
/// <c>409 freizeit-abgeschlossen</c>.
/// </summary>
internal static class KategorieFeldAendernEndpunkt
{
    public static async Task<IResult> Aendern(Guid freizeitId, string kategorie, JsonElement koerper, ISender sender, CancellationToken abbruch)
    {
        if (Kostenkategorien.Lesen(kategorie) is not { } gelesen)
        {
            return TypedResults.NotFound();
        }

        var (version, alterWert, feld, neuerWert) = FeldPatch.Lesen<KategorieFeld>(koerper, KategorieFelder.JsonName);
        return await sender.Send(new KategorieFeldAendernCommand(freizeitId, gelesen, feld, version, alterWert, neuerWert), abbruch) switch
        {
            FinanzenAenderung<KategoriePosten>.Gespeichert gespeichert => TypedResults.Ok(new Dictionary<string, object?>
            {
                ["version"] = gespeichert.Zeile.Version,
                [KategorieFelder.JsonName(feld)] = gespeichert.Zeile.Wert(feld),
            }),
            FinanzenAenderung<KategoriePosten>.Konflikt => FinanzenProbleme.Konflikt(),
            FinanzenAenderung<KategoriePosten>.Abgeschlossen => FinanzenProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
    }
}
