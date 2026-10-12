using System.Collections.Concurrent;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mail;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.Testmodul.Daten;

namespace Zeltlotse.Testmodul.Mails;

/// <summary>Mail-Auftrag des Testmoduls: nur die Kennung der Notiz.</summary>
internal sealed record NotizMail(Guid NotizId) : IMailAuftrag;

/// <summary>Merkt an einer Notiz eine Mail vor (wie „Erinnern“ in einem Fachmodul).</summary>
internal sealed record NotizMailVormerken(Guid NotizId)
    : ICommand, IModulCommand<TestmodulDbContext>, IBenutzerCommand;

internal sealed class NotizMailVormerkenHandler(TestmodulDbContext db) : ICommandHandler<NotizMailVormerken>
{
    public async ValueTask<Unit> Handle(NotizMailVormerken command, CancellationToken cancellationToken)
    {
        var notiz = await db.Notizen.SingleAsync(n => n.Id == command.NotizId, cancellationToken);
        notiz.MailVormerken();
        return Unit.Value;
    }
}

/// <summary>Steuert den Aufbau: für eine Notiz „nichts zu senden“ (<c>null</c>) oder einmal scheitern.</summary>
internal sealed class MailAufbauStoerung
{
    private readonly ConcurrentDictionary<Guid, bool> _ohneMail = new();
    private readonly ConcurrentDictionary<Guid, bool> _scheitertEinmal = new();

    public void OhneMail(Guid notizId) => _ohneMail[notizId] = true;

    public void ScheitertEinmal(Guid notizId) => _scheitertEinmal[notizId] = true;

    public bool IstOhneMail(Guid notizId) => _ohneMail.ContainsKey(notizId);

    public void PruefenUndAusloesen(Guid notizId)
    {
        if (_scheitertEinmal.TryRemove(notizId, out _))
        {
            throw new InvalidOperationException("Absichtlicher Fehler beim Aufbau der Mail.");
        }
    }
}

/// <summary>Baut die Mail erst beim Versand: Text der Notiz aus der Datenbank, Mandant aus dem Kontext der Zustellung.</summary>
internal sealed class NotizMailAufbau(TestmodulLeseDbContext db, IMandantenKontext mandant, MailAufbauStoerung stoerung) : IMailAufbau<NotizMail>
{
    public async Task<MailInhalt?> AufbauenAsync(NotizMail auftrag, CancellationToken abbruch)
    {
        stoerung.PruefenUndAusloesen(auftrag.NotizId);
        if (stoerung.IstOhneMail(auftrag.NotizId))
        {
            return null;
        }

        var text = await db.Notizen.Where(n => n.Id == auftrag.NotizId).Select(n => n.Text).SingleAsync(abbruch);
        return new MailInhalt("notiz@example.org", "Notiz", $"{text} | Mandant {mandant.OrganisationId}");
    }
}
