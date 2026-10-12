using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Personen.Daten;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Commands;
using NachweisArt = Zeltlotse.Personen.Contracts.Events.NachweisArt;

namespace Zeltlotse.Personen.Nachweise;

/// <summary>
/// Legt einen Nachweis am eigenen Profil an (FR-8), noch ohne Datei: Die Datei folgt getrennt, damit der Eintrag erhalten bleibt,
/// wenn der Upload scheitert (EXPERIENCE). Je Art höchstens ein Nachweis (<see cref="NachweisAnlage.Vorhanden"/> → <c>409</c>).
/// </summary>
internal sealed record NachweisAnlegen(Guid PersonId, NachweisArt? Art, DateOnly? Ausstellungsdatum)
    : ICommand<NachweisAnlage>, IModulCommand<PersonenDbContext>, IBenutzerCommand
{
    public override string ToString() => $"{nameof(NachweisAnlegen)}({PersonId}, {Art})";
}

/// <summary>Ergebnis des Anlegens.</summary>
internal abstract record NachweisAnlage
{
    private NachweisAnlage()
    {
    }

    public sealed record Angelegt(Guid NachweisId) : NachweisAnlage;

    /// <summary>Die Person hat schon einen Nachweis dieser Art: <c>409</c>.</summary>
    public sealed record Vorhanden : NachweisAnlage;
}

internal sealed class NachweisAnlegenValidator : AbstractValidator<NachweisAnlegen>
{
    public NachweisAnlegenValidator(TimeProvider uhr)
    {
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (NachweisRegeln.Art(c.Art) is { } art)
            {
                kontext.AddFailure(new ValidationFailure(NachweisFelder.Art, art));
            }

            if (NachweisRegeln.Ausstellungsdatum(c.Ausstellungsdatum, NachweisRegeln.Heute(uhr)) is { } datum)
            {
                kontext.AddFailure(new ValidationFailure(NachweisFelder.Ausstellungsdatum, datum));
            }
        });
    }
}

internal sealed class NachweisAnlegenHandler(PersonenDbContext db, TimeProvider uhr) : ICommandHandler<NachweisAnlegen, NachweisAnlage>
{
    public async ValueTask<NachweisAnlage> Handle(NachweisAnlegen command, CancellationToken cancellationToken)
    {
        var art = command.Art!.Value;
        if (await db.Nachweise.AnyAsync(n => n.PersonId == command.PersonId && n.Art == art, cancellationToken))
        {
            return new NachweisAnlage.Vorhanden();
        }

        var nachweis = Nachweis.Anlegen(Kennung.Neu(uhr), command.PersonId, art, command.Ausstellungsdatum!.Value);
        db.Nachweise.Add(nachweis);
        return new NachweisAnlage.Angelegt(nachweis.Id);
    }
}
