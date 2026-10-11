using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Konten.Daten;
using Zeltlotse.Konten.Domaene;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Zugangslinks;

namespace Zeltlotse.Konten.Anmelden;

/// <summary>
/// Nur für die Demo-Daten des API-Hosts (NFR-20): legt das Konto einer Mail-Adresse an, wenn es fehlt.
/// Kein Endpunkt; Menschen legen ihr Konto über den Login-Link an.
/// </summary>
internal sealed record KontoSicherstellen(string Mail) : ICommand<SichergestelltesKonto>, IModulCommand<KontenDbContext>
{
    /// <summary>Nie die Mail-Adresse ausgeben (AD-22).</summary>
    public override string ToString() => nameof(KontoSicherstellen);
}

/// <param name="Neu">Ob das Konto eben angelegt wurde.</param>
internal sealed record SichergestelltesKonto(Guid KontoId, Guid PersonId, bool Neu);

internal sealed class KontoSicherstellenHandler(KontenDbContext db, TimeProvider uhr) : ICommandHandler<KontoSicherstellen, SichergestelltesKonto>
{
    public async ValueTask<SichergestelltesKonto> Handle(KontoSicherstellen command, CancellationToken cancellationToken)
    {
        var mail = MailAdresse.Normalisieren(command.Mail);
        var konto = await db.Konten.SingleOrDefaultAsync(k => k.Mail == mail, cancellationToken);
        if (konto is not null)
        {
            return new SichergestelltesKonto(konto.Id, konto.PersonId, Neu: false);
        }

        konto = Konto.Anlegen(Kennung.Neu(uhr), mail, Kennung.Neu(uhr), uhr.GetUtcNow());
        db.Konten.Add(konto);
        return new SichergestelltesKonto(konto.Id, konto.PersonId, Neu: true);
    }
}
