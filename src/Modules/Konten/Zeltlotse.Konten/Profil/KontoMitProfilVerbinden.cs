using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Konten.Daten;
using Zeltlotse.Personen.Contracts.Events;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Ereignisse;

namespace Zeltlotse.Konten.Profil;

/// <summary>
/// Empfänger von <see cref="ProfilUebergabeAngenommen"/> (Tabelle Ereignis-Fluss, AD-4, AD-7): Das Konto der volljährigen
/// Person gehört ab jetzt zum übernommenen Profil. Der neue Sitzungsstempel beendet alle bisherigen Sitzungen; danach meldet
/// sich die Person neu an. Kein Benutzer-Command.
/// </summary>
internal sealed record KontoMitProfilVerbinden(Umschlag Umschlag, Guid PersonId, Guid KontoId)
    : ICommand, IModulCommand<KontenDbContext>, IEreignisEmpfaenger<KontoMitProfilVerbinden, ProfilUebergabeAngenommen>
{
    public static KontoMitProfilVerbinden Aus(Umschlag umschlag, ProfilUebergabeAngenommen ereignis) =>
        new(umschlag, ereignis.PersonId, ereignis.KontoId);
}

internal sealed class KontoMitProfilVerbindenHandler(KontenDbContext db) : ICommandHandler<KontoMitProfilVerbinden>
{
    public async ValueTask<Unit> Handle(KontoMitProfilVerbinden command, CancellationToken cancellationToken)
    {
        // Ein inzwischen gelöschtes Konto hat nichts mehr zu verbinden.
        var konto = await db.Konten.SingleOrDefaultAsync(k => k.Id == command.KontoId, cancellationToken);
        konto?.ProfilUebernehmen(command.PersonId);
        return Unit.Value;
    }
}
