using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Organisationen.Daten;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;
using OrgRolleArt = Zeltlotse.SharedKernel.Rechte.OrgRolle;

namespace Zeltlotse.Organisationen.Einladungen;

/// <summary>
/// Lädt eine Mail-Adresse mit einer Rolle in die Organisation ein (FR-5, Org-Admin). Auch weitere Org-Admins und der
/// Org-Überblick entstehen so – ein Mechanismus statt zwei. Läuft im Mandanten-Kontext der Organisation (AD-6).
/// </summary>
internal sealed record EinladungAnlegenCommand(Guid OrganisationId, string? Mail, OrgRolleArt? Rolle, Guid EingeladenVon)
    : ICommand<NeueEinladung>, IModulCommand<OrganisationenDbContext>, IBenutzerCommand
{
    /// <summary>Nie die Mail-Adresse ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(EinladungAnlegenCommand)}({OrganisationId})";
}

internal sealed class EinladungAnlegenValidator : AbstractValidator<EinladungAnlegenCommand>
{
    public EinladungAnlegenValidator()
    {
        RuleFor(c => c.Mail).MailAdresse();
        RuleFor(c => c.Rolle)
            .NotNull().WithMessage("Rolle fehlt.")
            .IsInEnum().WithMessage("Diese Rolle gibt es nicht.");
    }
}

internal sealed class EinladungAnlegenHandler(OrganisationenDbContext db, IMandantenKontext mandant, TimeProvider uhr)
    : ICommandHandler<EinladungAnlegenCommand, NeueEinladung>
{
    public async ValueTask<NeueEinladung> Handle(EinladungAnlegenCommand command, CancellationToken cancellationToken)
    {
        if (mandant.OrganisationId != command.OrganisationId)
        {
            throw new InvalidOperationException("Einladungen entstehen nur im Mandanten-Kontext ihrer Organisation (AD-6).");
        }

        var organisation = await db.Organisationen.SingleAsync(o => o.Id == command.OrganisationId, cancellationToken);
        return await EinladungAusstellen.Async(
            db.Einladungen, organisation, command.Mail!, command.Rolle!.Value, command.EingeladenVon, uhr, cancellationToken);
    }
}
