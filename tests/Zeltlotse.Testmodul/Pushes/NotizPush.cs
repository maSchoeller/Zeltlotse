using System.Collections.Concurrent;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Push;
using Zeltlotse.Testmodul.Daten;

namespace Zeltlotse.Testmodul.Pushes;

/// <summary>Story 6-1: Push-Auftrag des Testmoduls: nur die Kennung der Notiz.</summary>
internal sealed record NotizPush(Guid NotizId) : IPushAuftrag;

/// <summary>Merkt an einer Notiz einen Push vor (wie „Nachricht senden“ in den Kanälen).</summary>
internal sealed record NotizPushVormerken(Guid NotizId)
    : ICommand, IModulCommand<TestmodulDbContext>, IBenutzerCommand;

internal sealed class NotizPushVormerkenHandler(TestmodulDbContext db) : ICommandHandler<NotizPushVormerken>
{
    public async ValueTask<Unit> Handle(NotizPushVormerken command, CancellationToken cancellationToken)
    {
        var notiz = await db.Notizen.SingleAsync(n => n.Id == command.NotizId, cancellationToken);
        notiz.PushVormerken();
        return Unit.Value;
    }
}

/// <summary>Steuert den Aufbau: die Ziele, „nichts zu senden“, einmal scheitern; hält die als ungültig gemeldeten Ziele fest.</summary>
internal sealed class PushAufbauStoerung
{
    private readonly ConcurrentDictionary<Guid, bool> _scheitertEinmal = new();

    public List<PushZiel> Ziele { get; } = [];

    public bool OhnePush { get; set; }

    public bool AufraeumenScheitert { get; set; }

    public ConcurrentQueue<Guid> Ungueltig { get; } = new();

    public void ScheitertEinmal(Guid notizId) => _scheitertEinmal[notizId] = true;

    public void PruefenUndAusloesen(Guid notizId)
    {
        if (_scheitertEinmal.TryRemove(notizId, out _))
        {
            throw new InvalidOperationException("Absichtlicher Fehler beim Aufbau des Push.");
        }
    }
}

/// <summary>Baut den Push erst beim Versand: Text der Notiz aus der Datenbank, Mandant aus dem Kontext der Zustellung, Ziele aus der Störung.</summary>
internal sealed class NotizPushAufbau(TestmodulLeseDbContext db, IMandantenKontext mandant, PushAufbauStoerung stoerung) : IPushAufbau<NotizPush>
{
    public async Task<PushSendung?> AufbauenAsync(NotizPush auftrag, CancellationToken abbruch)
    {
        stoerung.PruefenUndAusloesen(auftrag.NotizId);
        if (stoerung.OhnePush)
        {
            return null;
        }

        var text = await db.Notizen.Where(n => n.Id == auftrag.NotizId).Select(n => n.Text).SingleAsync(abbruch);
        return new PushSendung(new PushInhalt("Notiz", $"{text} | Mandant {mandant.OrganisationId}", "/app/notizen"), [.. stoerung.Ziele]);
    }

    public Task UngueltigeZieleAsync(IReadOnlyCollection<Guid> zielIds, CancellationToken abbruch)
    {
        if (stoerung.AufraeumenScheitert)
        {
            throw new InvalidOperationException("Absichtlicher Fehler beim Aufräumen.");
        }

        foreach (var id in zielIds)
        {
            stoerung.Ungueltig.Enqueue(id);
        }

        return Task.CompletedTask;
    }
}
