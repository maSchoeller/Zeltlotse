using FluentValidation;
using Mediator;
using Zeltlotse.Organisationen.Daten;
using Zeltlotse.Organisationen.Domaene;
using Zeltlotse.Organisationen.Einladungen;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Zugangslinks;
using OrgRolleArt = Zeltlotse.SharedKernel.Rechte.OrgRolle;

namespace Zeltlotse.Organisationen.Verwaltung;

/// <summary>
/// Legt eine Organisation an und lädt den ersten Org-Admin per Mail-Adresse ein (FR-5, Global Admin). Die Einladung ist
/// die erste der Organisation; sie entsteht im selben Schritt, damit es keine Organisation ohne Weg zum Org-Admin gibt.
/// </summary>
internal sealed record OrganisationAnlegenCommand(string? Name, string? Mail, Guid EingeladenVon)
    : ICommand<NeueEinladung>, IModulCommand<OrganisationenDbContext>, IBenutzerCommand
{
    /// <summary>Nie Name oder Mail-Adresse ausgeben (AD-22).</summary>
    public override string ToString() => nameof(OrganisationAnlegenCommand);
}

internal sealed class OrganisationAnlegenValidator : AbstractValidator<OrganisationAnlegenCommand>
{
    public OrganisationAnlegenValidator()
    {
        RuleFor(c => c.Name)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Name fehlt.")
            .DependentRules(() => RuleFor(c => c.Name!.Trim())
                .MaximumLength(Organisation.MaximaleNamensLaenge).WithMessage($"Der Name ist zu lang (höchstens {Organisation.MaximaleNamensLaenge} Zeichen).")
                .OverridePropertyName(nameof(OrganisationAnlegenCommand.Name)));
        RuleFor(c => c.Mail).MailAdresse();
    }
}

internal sealed class OrganisationAnlegenHandler(OrganisationenDbContext db, TimeProvider uhr)
    : ICommandHandler<OrganisationAnlegenCommand, NeueEinladung>
{
    public async ValueTask<NeueEinladung> Handle(OrganisationAnlegenCommand command, CancellationToken cancellationToken)
    {
        var organisation = Organisation.Anlegen(Kennung.Neu(uhr), command.Name!, uhr.GetUtcNow());
        db.Organisationen.Add(organisation);
        return await EinladungAusstellen.Async(
            db.Einladungen, organisation, command.Mail!, OrgRolleArt.OrgAdmin, command.EingeladenVon, uhr, cancellationToken);
    }
}
