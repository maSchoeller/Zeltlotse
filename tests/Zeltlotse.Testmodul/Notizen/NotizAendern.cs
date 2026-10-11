using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.Testmodul.Daten;

namespace Zeltlotse.Testmodul.Notizen;

internal sealed record NotizAendern(Guid NotizId, string Text)
    : ICommand, IModulCommand<TestmodulDbContext>, IBenutzerCommand;

internal sealed class NotizAendernHandler(TestmodulDbContext db) : ICommandHandler<NotizAendern>
{
    public async ValueTask<Unit> Handle(NotizAendern command, CancellationToken cancellationToken)
    {
        var notiz = await db.Notizen.SingleAsync(n => n.Id == command.NotizId, cancellationToken);
        notiz.Aendern(command.Text);
        return Unit.Value;
    }
}
