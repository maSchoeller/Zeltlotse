using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Api.Contracts.Inventar;
using Zeltlotse.Inventar.Daten;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Inventar.Artikel;

/// <summary>Story 3-3: Legt einen Artikel im Inventar der Organisation an (Org-Admin, FR-37). Nur die Bezeichnung ist nötig.</summary>
internal sealed record ArtikelAnlegenCommand(Guid OrganisationId, string? Bezeichnung, string? Menge)
    : ICommand<InventarArtikel>, IModulCommand<InventarDbContext>, IBenutzerCommand;

internal sealed class ArtikelAnlegenValidator : AbstractValidator<ArtikelAnlegenCommand>
{
    public ArtikelAnlegenValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (InventarTexte.BezeichnungFehler(c.Bezeichnung) is { } bezeichnungFehler)
            {
                kontext.AddFailure(new ValidationFailure(InventarFelder.Bezeichnung, bezeichnungFehler));
            }

            if (InventarTexte.MengeFehler(c.Menge) is { } mengeFehler)
            {
                kontext.AddFailure(new ValidationFailure(InventarFelder.Menge, mengeFehler));
            }
        });
}

internal sealed class ArtikelAnlegenHandler(InventarDbContext db, TimeProvider uhr) : ICommandHandler<ArtikelAnlegenCommand, InventarArtikel>
{
    public ValueTask<InventarArtikel> Handle(ArtikelAnlegenCommand command, CancellationToken cancellationToken)
    {
        var artikel = InventarArtikel.Anlegen(Guid.CreateVersion7(uhr.GetUtcNow()), command.OrganisationId, command.Bezeichnung!, command.Menge);
        db.Artikel.Add(artikel);
        return ValueTask.FromResult(artikel);
    }
}

/// <summary>
/// <c>POST /api/organisationen/{orgId}/inventar</c> (Org-Admin, FR-37): Körper <see cref="InventarArtikelNeu"/>. <c>201</c> mit dem Artikel (mit
/// Version, ohne Reservierungen; <c>Location</c> ist das Inventar); Bezeichnung leer oder zu lang, Menge zu lang <c>400</c> am Feld.
/// </summary>
internal static class ArtikelAnlegenEndpunkt
{
    public static async Task<IResult> Anlegen(Guid orgId, InventarArtikelNeu koerper, ISender sender, CancellationToken abbruch)
    {
        var artikel = await sender.Send(new ArtikelAnlegenCommand(orgId, koerper.Bezeichnung, koerper.Menge), abbruch);
        return TypedResults.Created($"/api/organisationen/{orgId}/inventar", ArtikelLesen.AlsApi(artikel, []));
    }
}
