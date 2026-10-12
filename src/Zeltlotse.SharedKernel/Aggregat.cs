using System.ComponentModel.DataAnnotations.Schema;
using Zeltlotse.SharedKernel.Mail;

namespace Zeltlotse.SharedKernel;

/// <summary>
/// Entität, die Ereignisse erzeugt (AD-28). Eine Statusmethode legt das Ereignis mit <see cref="EreignisAblegen"/> ab
/// und erhöht damit die <see cref="Folgenummer"/> um 1. Nur die Outbox-Stufe der Pipeline sammelt die Ereignisse ein.
/// </summary>
public abstract class Aggregat
{
    private readonly List<AbgelegtesEreignis> _ereignisse = [];

    /// <summary>Kennung des Datensatzes; steht als Datensatz-Kennung im Umschlag.</summary>
    public Guid Id { get; protected init; }

    /// <summary>Versionsnummer im Umschlag; zählt jedes erzeugte Ereignis.</summary>
    public long Folgenummer { get; private set; }

    /// <summary>Noch nicht in die Outbox geschriebene Ereignisse.</summary>
    [NotMapped]
    public IReadOnlyList<object> Ereignisse => _ereignisse.Select(e => e.Ereignis).ToList();

    protected void EreignisAblegen(object ereignis)
    {
        ArgumentNullException.ThrowIfNull(ereignis);
        Folgenummer++;
        _ereignisse.Add(new AbgelegtesEreignis(ereignis, Folgenummer));
    }

    /// <summary>
    /// Merkt einen Mail-Auftrag vor (AD-13): Wie ein Ereignis erhöht er die <see cref="Folgenummer"/> und landet über die
    /// Outbox-Stufe im selben <c>SaveChanges</c> in der Outbox, mit genau einer Zustellung an <see cref="MailAuftraege.Empfaenger"/>.
    /// </summary>
    protected void MailVormerken(IMailAuftrag auftrag)
    {
        ArgumentNullException.ThrowIfNull(auftrag);
        Folgenummer++;
        _ereignisse.Add(new AbgelegtesEreignis(auftrag, Folgenummer));
    }

    internal IReadOnlyList<AbgelegtesEreignis> AbgelegteEreignisse => _ereignisse;

    internal void EreignisseLeeren() => _ereignisse.Clear();
}

internal sealed record AbgelegtesEreignis(object Ereignis, long Folgenummer);
