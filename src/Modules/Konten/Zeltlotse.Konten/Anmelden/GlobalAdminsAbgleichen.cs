using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Konten.Daten;
using Zeltlotse.Konten.Domaene;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;
using Zeltlotse.SharedKernel.Zugangslinks;

namespace Zeltlotse.Konten.Anmelden;

/// <summary>
/// Gleicht das Global-Admin-Kennzeichen mit der Liste der Mail-Adressen ab (Start des API-Hosts, kein Endpunkt): Genau die
/// Konten dieser Mail-Adressen sind danach Global Admin, alle anderen nicht mehr. Fehlt zu einer Mail-Adresse das Konto,
/// entsteht es hier, mit neuer <c>PersonId</c> wie beim ersten Login-Link. Die Adressen prüft vorher die Startprüfung.
/// Jede tatsächliche Änderung des Kennzeichens (vergeben, entzogen, neues Konto) steht als <c>Rollenvergabe</c> mit der
/// <c>PersonId</c> des Kontos im Sicherheitsprotokoll (AD-22); ohne Änderung kein Eintrag.
/// </summary>
internal sealed record GlobalAdminsAbgleichen(IReadOnlyCollection<string> Mails)
    : ICommand<GlobalAdminAbgleich>, IModulCommand<KontenDbContext>
{
    /// <summary>Nie die Mail-Adressen ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(GlobalAdminsAbgleichen)}({Mails.Count})";
}

/// <param name="GlobalAdmins">Anzahl der Global Admins nach dem Abgleich.</param>
/// <param name="NeueKonten">Anzahl der dabei angelegten Konten.</param>
internal sealed record GlobalAdminAbgleich(int GlobalAdmins, int NeueKonten);

internal sealed class GlobalAdminsAbgleichenHandler(KontenDbContext db, TimeProvider uhr, ISicherheitsprotokoll protokoll)
    : ICommandHandler<GlobalAdminsAbgleichen, GlobalAdminAbgleich>
{
    public async ValueTask<GlobalAdminAbgleich> Handle(GlobalAdminsAbgleichen command, CancellationToken cancellationToken)
    {
        var mails = command.Mails.Select(MailAdresse.Normalisieren).ToHashSet(StringComparer.Ordinal);

        var geaendert = new List<Guid>();
        var bisher = await db.Konten.Where(k => k.GlobalAdmin && !mails.Contains(k.Mail)).ToListAsync(cancellationToken);
        foreach (var konto in bisher)
        {
            konto.GlobalAdminSetzen(false);
            geaendert.Add(konto.PersonId);
        }

        var vorhanden = await db.Konten.Where(k => mails.Contains(k.Mail)).ToListAsync(cancellationToken);
        foreach (var konto in vorhanden.Where(k => !k.GlobalAdmin))
        {
            konto.GlobalAdminSetzen(true);
            geaendert.Add(konto.PersonId);
        }

        var neu = 0;
        foreach (var mail in mails.Where(m => vorhanden.All(k => k.Mail != m)))
        {
            var konto = Konto.Anlegen(Kennung.Neu(uhr), mail, Kennung.Neu(uhr), uhr.GetUtcNow());
            konto.GlobalAdminSetzen(true);
            db.Konten.Add(konto);
            geaendert.Add(konto.PersonId);
            neu++;
        }

        foreach (var personId in geaendert)
        {
            await protokoll.SchreibenAsync(SicherheitsEreignisArt.Rollenvergabe, personId, abbruch: cancellationToken);
        }

        return new GlobalAdminAbgleich(mails.Count, neu);
    }
}
