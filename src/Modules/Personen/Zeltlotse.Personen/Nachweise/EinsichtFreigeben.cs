using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Personen.Daten;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Personen.Nachweise;

/// <summary>
/// Die Person gibt eine eigene Einsicht für <see cref="NachweisEinsicht.Freigabedauer"/> frei (FR-10), wenn sie angefragt ist
/// oder ihre Freigabe abgelaufen ist. Personenbezogener Weg über Organisationen hinweg (AD-26): Die Einsicht wird ohne
/// Mandanten-Kontext gelesen, aber nur, wenn sie der Person gehört. Fremd oder abgeschlossen → <c>404</c>; gültige Freigabe läuft
/// schon oder kein passender Nachweis mit Datei → <c>409</c>.
/// </summary>
internal sealed record EinsichtFreigeben(Guid PersonId, Guid EinsichtId)
    : ICommand<EinsichtFreigabe>, IModulCommand<PersonenDbContext>, IBenutzerCommand;

/// <summary>Ergebnis der Freigabe.</summary>
internal abstract record EinsichtFreigabe
{
    private EinsichtFreigabe()
    {
    }

    public sealed record Freigegeben(DateTimeOffset Bis) : EinsichtFreigabe;

    public sealed record NichtGefunden : EinsichtFreigabe;

    public sealed record OhneDatei : EinsichtFreigabe;

    /// <summary>Die Freigabe ist noch gültig: <c>409</c>.</summary>
    public sealed record LaeuftSchon : EinsichtFreigabe;
}

internal sealed class EinsichtFreigebenHandler(PersonenDbContext db, TimeProvider uhr) : ICommandHandler<EinsichtFreigeben, EinsichtFreigabe>
{
    public async ValueTask<EinsichtFreigabe> Handle(EinsichtFreigeben command, CancellationToken cancellationToken)
    {
        var einsicht = await NachweisSperre.EinsichtAsync(db.Einsichten, db.EinsichtPosten, command.EinsichtId, cancellationToken, ohneMandant: true);
        if (einsicht is null || einsicht.PersonId != command.PersonId || einsicht.Status == EinsichtStatus.Abgeschlossen)
        {
            return new EinsichtFreigabe.NichtGefunden();
        }

        var jetzt = uhr.GetUtcNow();
        if (!einsicht.Freigebbar(jetzt))
        {
            return new EinsichtFreigabe.LaeuftSchon();
        }

        var mitDatei = (await db.Nachweise
                .Where(n => n.PersonId == command.PersonId && n.HochgeladenAm != null)
                .Select(n => new { n.Id, n.Art })
                .ToListAsync(cancellationToken))
            .Select(n => (n.Id, n.Art))
            .ToList();
        return einsicht.Freigeben(mitDatei, jetzt)
            ? new EinsichtFreigabe.Freigegeben(einsicht.FreigegebenBis!.Value)
            : new EinsichtFreigabe.OhneDatei();
    }
}
