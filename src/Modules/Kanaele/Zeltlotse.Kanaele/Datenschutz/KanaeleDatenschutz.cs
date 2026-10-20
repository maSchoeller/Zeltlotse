using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Kanaele;
using Zeltlotse.Kanaele.Daten;
using Zeltlotse.Personen.Contracts.Events;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Ereignisse;

namespace Zeltlotse.Kanaele.Datenschutz;

/// <summary>
/// Story 6-1: Empfänger von <see cref="PersonGeloescht"/> (Tabelle Ereignis-Fluss, AD-16). Nachrichten sind Arbeitsdaten der Freizeit: In allen
/// Organisationen wird die Autor-Kennung zum Platzhalter <see cref="GeloeschtePerson.Id"/>, der Text bleibt. Push-Abos sind personeneigen und werden sofort
/// gelöscht. Kein Benutzer-Command.
/// </summary>
internal sealed record KanaelePersonLoeschen(Umschlag Umschlag, Guid PersonId)
    : ICommand, IModulCommand<KanaeleDbContext>, IEreignisEmpfaenger<KanaelePersonLoeschen, PersonGeloescht>
{
    public static KanaelePersonLoeschen Aus(Umschlag umschlag, PersonGeloescht ereignis) => new(umschlag, ereignis.PersonId);

    /// <summary>Keine Personenkennung ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(KanaelePersonLoeschen)}({Umschlag.EreignisId})";
}

internal sealed class KanaelePersonLoeschenHandler(KanaeleDbContext db) : ICommandHandler<KanaelePersonLoeschen>
{
    public async ValueTask<Unit> Handle(KanaelePersonLoeschen command, CancellationToken cancellationToken)
    {
        await db.Nachrichten.IgnoreQueryFilters().Where(n => n.AutorPersonId == command.PersonId)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.AutorPersonId, GeloeschtePerson.Id), cancellationToken);
        await db.PushAbos.IgnoreQueryFilters().Where(a => a.PersonId == command.PersonId).ExecuteDeleteAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>
/// Story 6-1: Datenauskunft der Kanäle (AD-16): Abschnitt „Kanäle“ mit den eigenen Nachrichten (Kanal, gesendet am, Text) und der Zahl der eigenen Push-Abos.
/// Ohne Mandantenfilter, nur die Zeilen der Person.
/// </summary>
internal sealed class KanaeleAuskunft(KanaeleLeseDbContext db) : IDatenauskunft
{
    public async Task<IReadOnlyList<AuskunftAbschnitt>> AuskunftAsync(Guid personId, CancellationToken abbruch)
    {
        var nachrichten = await db.Nachrichten.IgnoreQueryFilters()
            .Where(n => n.AutorPersonId == personId)
            .OrderBy(n => n.GesendetAm)
            .ThenBy(n => n.Laufnummer)
            .Select(n => new { n.Kanal, n.GesendetAm, n.Text })
            .ToListAsync(abbruch);
        var abos = await db.PushAbos.IgnoreQueryFilters().CountAsync(a => a.PersonId == personId, abbruch);
        List<AuskunftZeile> zeilen =
        [
            .. nachrichten.Select(n => new AuskunftZeile(
                $"Nachricht im Kanal „{(n.Kanal == Kanal.Alle ? "Alle" : "Team")}“ vom {AuskunftFormat.Datum(n.GesendetAm)}", n.Text)),
        ];
        if (abos > 0)
        {
            zeilen.Add(new AuskunftZeile("Push-Abos (Geräte mit Benachrichtigungen)", abos.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        return [new AuskunftAbschnitt("Kanäle", zeilen)];
    }
}
