using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Kanaele.Daten;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Kanaele.Notizen;

/// <summary>
/// Story 6-3 (FR-63): Hakt einen Punkt ab (<see cref="Erledigt"/> <c>true</c>) oder öffnet ihn wieder (<c>false</c>). Jede Person aus Leitung und Team darf
/// das, auch wiederholt: Ein schon abgehakter Punkt behält seinen ersten Zeitpunkt. Eine abgeschlossene Freizeit ist nur lesbar (AD-17, Pipeline).
/// </summary>
internal sealed record NotizUebergangCommand(Guid FreizeitId, Guid NotizId, bool Erledigt)
    : ICommand<Notiz?>, IModulCommand<KanaeleDbContext>, IBenutzerCommand, IFreizeitCommand
{
    /// <summary>Nur Kennungen ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(NotizUebergangCommand)}({FreizeitId}, {NotizId}, {Erledigt})";
}

/// <summary>Liefert den geänderten Punkt; <c>null</c>, wenn es ihn in dieser Freizeit nicht gibt.</summary>
internal sealed class NotizUebergangHandler(KanaeleDbContext db, TimeProvider uhr) : ICommandHandler<NotizUebergangCommand, Notiz?>
{
    public async ValueTask<Notiz?> Handle(NotizUebergangCommand command, CancellationToken cancellationToken)
    {
        var notiz = await db.Notizen.SingleOrDefaultAsync(n => n.FreizeitId == command.FreizeitId && n.Id == command.NotizId, cancellationToken);
        if (notiz is null)
        {
            return null;
        }

        if (command.Erledigt)
        {
            notiz.Abhaken(uhr.GetUtcNow());
        }
        else
        {
            notiz.Oeffnen();
        }

        return notiz;
    }
}

/// <summary>
/// <c>POST /api/freizeiten/{freizeitId}/kanaele/notizen/{notizId}/erledigt</c> und <c>…/offen</c> (Leitung und Team): <c>200</c> mit dem Punkt, auch
/// wiederholt; unbekannter Punkt oder Punkt einer anderen Freizeit <c>404</c>; abgeschlossene Freizeit <c>409 freizeit-abgeschlossen</c>.
/// </summary>
internal static class NotizUebergangEndpunkt
{
    public static Task<IResult> Erledigt(Guid freizeitId, Guid notizId, ISender sender, CancellationToken abbruch) =>
        AusfuehrenAsync(freizeitId, notizId, erledigt: true, sender, abbruch);

    public static Task<IResult> Offen(Guid freizeitId, Guid notizId, ISender sender, CancellationToken abbruch) =>
        AusfuehrenAsync(freizeitId, notizId, erledigt: false, sender, abbruch);

    private static async Task<IResult> AusfuehrenAsync(Guid freizeitId, Guid notizId, bool erledigt, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new NotizUebergangCommand(freizeitId, notizId, erledigt), abbruch) is { } notiz
            ? TypedResults.Ok(NotizenLadenEndpunkt.AlsApi(notiz))
            : TypedResults.NotFound();
}
