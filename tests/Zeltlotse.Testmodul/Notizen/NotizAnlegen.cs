using FluentValidation;
using Mediator;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.Testmodul.Daten;

namespace Zeltlotse.Testmodul.Notizen;

internal sealed record NotizAnlegen(Guid OrganisationId, string Text)
    : ICommand<Guid>, IModulCommand<TestmodulDbContext>, IBenutzerCommand;

internal sealed class NotizAnlegenValidator : AbstractValidator<NotizAnlegen>
{
    public NotizAnlegenValidator()
    {
        RuleFor(c => c.Text).NotEmpty().MaximumLength(200);
    }
}

internal sealed class NotizAnlegenHandler(TestmodulDbContext db, TimeProvider uhr) : ICommandHandler<NotizAnlegen, Guid>
{
    public ValueTask<Guid> Handle(NotizAnlegen command, CancellationToken cancellationToken)
    {
        var notiz = Notiz.Anlegen(Kennung.Neu(uhr), command.OrganisationId, command.Text);
        db.Notizen.Add(notiz);
        return ValueTask.FromResult(notiz.Id);
    }
}

internal sealed record NotizAnlegenUndScheitern(Guid OrganisationId, string Text)
    : ICommand<Guid>, IModulCommand<TestmodulDbContext>, IBenutzerCommand;

/// <summary>Speichert sogar zwischendurch und wirft dann: Nichts darf bleiben.</summary>
internal sealed class NotizAnlegenUndScheiternHandler(TestmodulDbContext db, TimeProvider uhr) : ICommandHandler<NotizAnlegenUndScheitern, Guid>
{
    public async ValueTask<Guid> Handle(NotizAnlegenUndScheitern command, CancellationToken cancellationToken)
    {
        db.Notizen.Add(Notiz.Anlegen(Kennung.Neu(uhr), command.OrganisationId, command.Text));
        await db.SaveChangesAsync(cancellationToken);
        throw new InvalidOperationException("Absichtlicher Fehler im Handler.");
    }
}
