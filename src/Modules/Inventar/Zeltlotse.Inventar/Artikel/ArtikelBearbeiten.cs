using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Api.Contracts.Inventar;
using Zeltlotse.Inventar.Daten;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Inventar.Artikel;

/// <summary>
/// Story 3-3: Ändert genau ein Feld eines Artikels (Bezeichnung oder Menge, AD-19). Konfliktregel: Weicht der aktuelle Wert desselben Felds vom
/// mitgeschickten alten Wert ab, ist das Ergebnis <see cref="ArtikelAenderung.Konflikt"/>.
/// </summary>
internal sealed record ArtikelFeldAendernCommand(Guid ArtikelId, InventarArtikelFeld Feld, uint Version, string? AlterWert, string? NeuerWert)
    : ICommand<ArtikelAenderung>, IModulCommand<InventarDbContext>, IBenutzerCommand;

/// <summary>Ergebnis einer Feldänderung; bei <see cref="Gespeichert"/> der verfolgte Artikel (nach dem Speichern mit neuer Version).</summary>
internal abstract record ArtikelAenderung
{
    private ArtikelAenderung()
    {
    }

    public sealed record Gespeichert(InventarArtikel Artikel) : ArtikelAenderung;

    public sealed record Konflikt : ArtikelAenderung;

    public sealed record NichtGefunden : ArtikelAenderung;
}

internal sealed class ArtikelFeldAendernValidator : AbstractValidator<ArtikelFeldAendernCommand>
{
    public ArtikelFeldAendernValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            switch (c.Feld)
            {
                case InventarArtikelFeld.Bezeichnung when InventarTexte.BezeichnungFehler(c.NeuerWert) is { } fehler:
                    kontext.AddFailure(new ValidationFailure(InventarFelder.Bezeichnung, fehler));
                    break;
                case InventarArtikelFeld.Menge when InventarTexte.MengeFehler(c.NeuerWert) is { } fehler:
                    kontext.AddFailure(new ValidationFailure(InventarFelder.Menge, fehler));
                    break;
                case InventarArtikelFeld.Bezeichnung or InventarArtikelFeld.Menge:
                    break;
                default:
                    kontext.AddFailure(new ValidationFailure("feld", "Unbekanntes Feld."));
                    break;
            }
        });
}

internal sealed class ArtikelFeldAendernHandler(InventarDbContext db) : ICommandHandler<ArtikelFeldAendernCommand, ArtikelAenderung>
{
    public async ValueTask<ArtikelAenderung> Handle(ArtikelFeldAendernCommand command, CancellationToken cancellationToken)
    {
        // Zeilensperre (FOR UPDATE): Änderungen laufen nacheinander, allein die Regel zum alten Wert entscheidet.
        var artikel = await db.ArtikelSperrenAsync(command.ArtikelId, cancellationToken);
        if (artikel is null)
        {
            return new ArtikelAenderung.NichtGefunden();
        }

        return artikel.FeldAendern(command.Feld, command.AlterWert, command.NeuerWert)
            ? new ArtikelAenderung.Gespeichert(artikel)
            : new ArtikelAenderung.Konflikt();
    }
}

/// <summary>
/// <c>PATCH /api/organisationen/{orgId}/inventar/{artikelId}</c> (Org-Admin, AD-19): Körper <c>{ "version": n, "alterWert": …,
/// "bezeichnung"|"menge": neu }</c> mit genau einem Feld. <c>200 { "version", "&lt;feld&gt;" }</c>; ungültiger Wert, kein, mehr als ein oder ein
/// unbekanntes Feld <c>400</c>; anderer Wert desselben Felds <c>409 konflikt</c>; Artikel einer anderen Organisation oder unbekannt <c>404</c>.
/// </summary>
internal static class ArtikelFeldAendernEndpunkt
{
    public static async Task<IResult> Aendern(Guid artikelId, JsonElement koerper, ISender sender, CancellationToken abbruch)
    {
        var (version, alterWert, feld, neuerWert) = FeldPatch.Lesen<InventarArtikelFeld>(koerper, InventarFelder.JsonName);
        return await sender.Send(new ArtikelFeldAendernCommand(artikelId, feld, version, alterWert, neuerWert), abbruch) switch
        {
            ArtikelAenderung.Gespeichert gespeichert => TypedResults.Ok(new Dictionary<string, object?>
            {
                ["version"] = gespeichert.Artikel.Version,
                [InventarFelder.JsonName(feld)] = gespeichert.Artikel.Wert(feld),
            }),
            ArtikelAenderung.Konflikt => InventarProbleme.Konflikt("Der Wert wurde inzwischen geändert."),
            _ => TypedResults.NotFound(),
        };
    }
}
