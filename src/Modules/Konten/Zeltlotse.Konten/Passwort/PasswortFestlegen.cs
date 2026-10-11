using FluentValidation;
using Mediator;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Konten.Daten;
using Zeltlotse.Konten.Domaene;
using Zeltlotse.Konten.Sitzungen;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Konten.Passwort;

/// <summary>
/// Legt das Passwort des eigenen Kontos fest oder ändert es. Der Sitzungsstempel wird neu: Andere Sitzungen enden,
/// die eigene stellt der Endpunkt mit dem neuen Stempel neu aus.
/// </summary>
internal sealed record PasswortFestlegenCommand(Guid KontoId, string? Passwort)
    : ICommand<SitzungsDaten>, IModulCommand<KontenDbContext>, IBenutzerCommand
{
    public override string ToString() => $"{nameof(PasswortFestlegenCommand)}({KontoId})";
}

internal sealed class PasswortFestlegenValidator : AbstractValidator<PasswortFestlegenCommand>
{
    public PasswortFestlegenValidator()
    {
        RuleFor(c => c.Passwort)
            .NotEmpty().WithMessage("Passwort fehlt.")
            .MinimumLength(PasswortRegel.MindestLaenge).WithMessage($"Das Passwort braucht mindestens {PasswortRegel.MindestLaenge} Zeichen.")
            .MaximumLength(PasswortRegel.HoechstLaenge).WithMessage($"Das Passwort hat höchstens {PasswortRegel.HoechstLaenge} Zeichen.");
    }
}

internal sealed class PasswortFestlegenHandler(KontenDbContext db, IPasswordHasher<Konto> hasher)
    : ICommandHandler<PasswortFestlegenCommand, SitzungsDaten>
{
    public async ValueTask<SitzungsDaten> Handle(PasswortFestlegenCommand command, CancellationToken cancellationToken)
    {
        var konto = await db.Konten.SingleAsync(k => k.Id == command.KontoId, cancellationToken);
        konto.PasswortFestlegen(hasher.HashPassword(konto, command.Passwort!));
        return new SitzungsDaten(konto.Id, konto.PersonId, konto.SitzungsStempel);
    }
}
