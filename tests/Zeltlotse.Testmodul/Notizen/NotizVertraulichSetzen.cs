using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.Testmodul.Daten;

namespace Zeltlotse.Testmodul.Notizen;

/// <summary>Speichert einen besonders schützenswerten Text ausdrücklich über die zentrale Verschlüsselung (AD-14).</summary>
internal sealed record NotizVertraulichSetzen(Guid NotizId, Guid PersonId, string Text)
    : ICommand, IModulCommand<TestmodulDbContext>, IBenutzerCommand;

internal sealed class NotizVertraulichSetzenHandler(TestmodulDbContext db, IVerschluesselung verschluesselung)
    : ICommandHandler<NotizVertraulichSetzen>
{
    public async ValueTask<Unit> Handle(NotizVertraulichSetzen command, CancellationToken cancellationToken)
    {
        var notiz = await db.Notizen.SingleAsync(n => n.Id == command.NotizId, cancellationToken);
        notiz.VertraulichSetzen(await verschluesselung.VerschluesselnAsync(command.PersonId, command.Text, cancellationToken));
        return Unit.Value;
    }
}
