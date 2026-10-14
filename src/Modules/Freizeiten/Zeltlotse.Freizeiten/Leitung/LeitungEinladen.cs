using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Freizeiten.Daten;
using Zeltlotse.Freizeiten.Domaene;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Zugangslinks;

namespace Zeltlotse.Freizeiten.Leitung;

/// <summary>
/// Lädt eine Mail-Adresse als Leitung einer Freizeit ein (FR-6, Org-Admin). Eine neue Einladung verwirft ältere offene
/// derselben Mail-Adresse für dieselbe Freizeit. Läuft im Mandanten-Kontext der Organisation (AD-6).
/// </summary>
internal sealed record LeitungEinladenCommand(Guid OrganisationId, Guid FreizeitId, string? Mail, Guid EingeladenVon)
    : ICommand<NeueLeitungsEinladung>, IModulCommand<FreizeitenDbContext>, IBenutzerCommand, IFreizeitCommand
{
    /// <summary>Nie die Mail-Adresse ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(LeitungEinladenCommand)}({FreizeitId})";
}

/// <summary>
/// Eine eben angelegte Leitungs-Einladung. Das Token im Klartext gibt es nur hier, für die Einladungs-Mail; gespeichert ist
/// nur der Hash (AD-11).
/// </summary>
internal sealed record NeueLeitungsEinladung(Guid EinladungId, Guid OrganisationId, Guid FreizeitId, string FreizeitName, string Mail, string Token)
{
    /// <summary>Nie Mail-Adresse oder Token ausgeben (AD-22).</summary>
    public override string ToString() => $"NeueLeitungsEinladung({EinladungId})";
}

internal sealed class LeitungEinladenValidator : AbstractValidator<LeitungEinladenCommand>
{
    public LeitungEinladenValidator() => RuleFor(c => c.Mail).MailAdresse();
}

internal sealed class LeitungEinladenHandler(FreizeitenDbContext db, IMandantenKontext mandant, TimeProvider uhr)
    : ICommandHandler<LeitungEinladenCommand, NeueLeitungsEinladung>
{
    public async ValueTask<NeueLeitungsEinladung> Handle(LeitungEinladenCommand command, CancellationToken cancellationToken)
    {
        if (mandant.OrganisationId != command.OrganisationId)
        {
            throw new InvalidOperationException("Einladungen entstehen nur im Mandanten-Kontext ihrer Organisation (AD-6).");
        }

        // Unter dem Mandantenfilter: Eine Freizeit einer anderen Organisation gibt es hier nicht.
        var freizeit = await db.Freizeiten.SingleAsync(f => f.Id == command.FreizeitId, cancellationToken);
        var normalisiert = MailAdresse.Normalisieren(command.Mail!);
        var offene = await db.Einladungen
            .Where(e => e.FreizeitId == freizeit.Id && e.Mail == normalisiert && e.Status == LeitungsEinladungStatus.Offen)
            .ToListAsync(cancellationToken);
        foreach (var aeltere in offene)
        {
            aeltere.Verwerfen();
        }

        var token = Zugangslink.NeuesToken();
        var einladung = LeitungsEinladung.Anlegen(
            Kennung.Neu(uhr), freizeit.OrganisationId, freizeit.Id, normalisiert, Zugangslink.TokenHash(LeitungsEinladung.Zweck, token), command.EingeladenVon, uhr.GetUtcNow());
        db.Einladungen.Add(einladung);
        return new NeueLeitungsEinladung(einladung.Id, freizeit.OrganisationId, freizeit.Id, freizeit.Name, normalisiert, token);
    }
}
