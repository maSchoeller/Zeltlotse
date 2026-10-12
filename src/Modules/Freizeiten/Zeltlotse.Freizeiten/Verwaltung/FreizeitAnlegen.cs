using FluentValidation;
using Mediator;
using Zeltlotse.Freizeiten.Daten;
using Zeltlotse.Freizeiten.Domaene;
using Zeltlotse.Freizeiten.Freizeitseite;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Zugangslinks;

namespace Zeltlotse.Freizeiten.Verwaltung;

/// <summary>
/// Legt eine Freizeit an (FR-6, Org-Admin): Name und Zeitraum sind Pflicht (NFR-12); das Freigabe-Intervall wird aus der
/// Vorgabe festgeschrieben (FR-24d). Mit der Freizeit entstehen ihre beiden Anmeldelinks (AD-11, FR-16). Läuft im
/// Mandanten-Kontext der Organisation (AD-6).
/// </summary>
internal sealed record FreizeitAnlegenCommand(Guid OrganisationId, string? Name, DateOnly? ErsterTag, DateOnly? LetzterTag, Guid AngelegtVon)
    : ICommand<Guid>, IModulCommand<FreizeitenDbContext>, IBenutzerCommand
{
    /// <summary>Den Namen nicht ausgeben; er kann Hinweise auf Personen enthalten (AD-22).</summary>
    public override string ToString() => $"{nameof(FreizeitAnlegenCommand)}({OrganisationId})";
}

internal sealed class FreizeitAnlegenValidator : AbstractValidator<FreizeitAnlegenCommand>
{
    private const string JahrMeldung = "Das Jahr muss zwischen 2000 und 2100 liegen.";

    public FreizeitAnlegenValidator()
    {
        RuleFor(c => c.Name)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Name fehlt.")
            .DependentRules(() => RuleFor(c => c.Name!.Trim())
                .MaximumLength(Freizeit.MaximaleNamensLaenge).WithMessage($"Der Name ist zu lang (höchstens {Freizeit.MaximaleNamensLaenge} Zeichen).")
                .OverridePropertyName(nameof(FreizeitAnlegenCommand.Name)));
        RuleFor(c => c.ErsterTag)
            .NotNull().WithMessage("Erster Tag fehlt.")
            .Must(ImRahmen).WithMessage(JahrMeldung);
        RuleFor(c => c.LetzterTag)
            .NotNull().WithMessage("Letzter Tag fehlt.")
            .Must(ImRahmen).WithMessage(JahrMeldung)
            .DependentRules(() => RuleFor(c => c.LetzterTag)
                .Must((c, letzter) => c.ErsterTag is not { } erster || !ImRahmen(erster) || letzter >= erster)
                .WithMessage("Der letzte Tag liegt vor dem ersten Tag."));
    }

    private static bool ImRahmen(DateOnly? tag) => tag is not { } wert || wert.Year is >= Freizeit.FruehestesJahr and <= Freizeit.SpaetestesJahr;
}

internal sealed class FreizeitAnlegenHandler(FreizeitenDbContext db, IMandantenKontext mandant, ILinkSchutz schutz, TimeProvider uhr)
    : ICommandHandler<FreizeitAnlegenCommand, Guid>
{
    public ValueTask<Guid> Handle(FreizeitAnlegenCommand command, CancellationToken cancellationToken)
    {
        if (mandant.OrganisationId != command.OrganisationId)
        {
            throw new InvalidOperationException("Freizeiten entstehen nur im Mandanten-Kontext ihrer Organisation (AD-6).");
        }

        var freizeit = Freizeit.Anlegen(
            Kennung.Neu(uhr),
            command.OrganisationId,
            command.Name!,
            command.ErsterTag!.Value,
            command.LetzterTag!.Value,
            FreizeitZeiten.FreigabeIntervallVorgabeTage,
            command.AngelegtVon,
            uhr.GetUtcNow());
        db.Freizeiten.Add(freizeit);
        foreach (var art in new[] { LinkArt.Teilnehmende, LinkArt.Mitarbeitende })
        {
            db.Anmeldelinks.Add(AnmeldelinkErzeugen.Neu(schutz, uhr, freizeit.OrganisationId, freizeit.Id, art));
        }

        return ValueTask.FromResult(freizeit.Id);
    }
}
