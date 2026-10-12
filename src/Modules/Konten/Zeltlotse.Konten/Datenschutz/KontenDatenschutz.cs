using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Konten.Daten;
using Zeltlotse.Personen.Contracts.Events;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Ereignisse;

namespace Zeltlotse.Konten.Datenschutz;

/// <summary>
/// Empfänger von <see cref="PersonGeloescht"/> (Tabelle Ereignis-Fluss, AD-16): löscht das Konto der Person und alle
/// Login-Anfragen zu seiner Mail-Adresse. Damit enden alle Sitzungen des Kontos über die Sitzungsprüfung; eine spätere Anmeldung
/// mit derselben Mail-Adresse beginnt mit einem neuen, leeren Konto. Kein Benutzer-Command.
/// </summary>
internal sealed record KontoLoeschen(Umschlag Umschlag, Guid PersonId)
    : ICommand, IModulCommand<KontenDbContext>, IEreignisEmpfaenger<KontoLoeschen, PersonGeloescht>
{
    public static KontoLoeschen Aus(Umschlag umschlag, PersonGeloescht ereignis) => new(umschlag, ereignis.PersonId);
}

internal sealed class KontoLoeschenHandler(KontenDbContext db) : ICommandHandler<KontoLoeschen>
{
    public async ValueTask<Unit> Handle(KontoLoeschen command, CancellationToken cancellationToken)
    {
        var mails = await db.Konten.Where(k => k.PersonId == command.PersonId).Select(k => k.Mail).ToListAsync(cancellationToken);
        await db.LoginAnfragen.Where(a => mails.Contains(a.Mail)).ExecuteDeleteAsync(cancellationToken);
        await db.Konten.Where(k => k.PersonId == command.PersonId).ExecuteDeleteAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>Datenauskunft von Konten (AD-16): Mail-Adresse, angelegt am und ob ein Passwort festgelegt ist; nie der Prüfwert.</summary>
internal sealed class KontenAuskunft(KontenLeseDbContext db) : IDatenauskunft
{
    public async Task<IReadOnlyList<AuskunftAbschnitt>> AuskunftAsync(Guid personId, CancellationToken abbruch)
    {
        var konto = await db.Konten
            .Where(k => k.PersonId == personId)
            .Select(k => new { k.Mail, k.AngelegtAm, HatPasswort = k.PasswortHash != null })
            .SingleOrDefaultAsync(abbruch);
        return konto is null
            ? []
            :
            [
                new AuskunftAbschnitt("Konto",
                [
                    new AuskunftZeile("Mail-Adresse", konto.Mail),
                    new AuskunftZeile("Angelegt am", AuskunftFormat.Datum(konto.AngelegtAm)),
                    new AuskunftZeile("Passwort festgelegt", AuskunftFormat.JaNein(konto.HatPasswort)),
                ]),
            ];
    }
}

/// <summary>Täglicher Lösch-Lauf von Konten (AD-16): schickt nur <see cref="LoginAnfragenLoeschen"/>.</summary>
internal sealed class KontenLoeschlauf(ISender sender) : ILoeschlauf
{
    public async Task LaufAsync(LoeschAuftrag auftrag, CancellationToken abbruch) =>
        await sender.Send(new LoginAnfragenLoeschen(auftrag.Jetzt), abbruch);
}

/// <summary>Login-Anfragen, deren Ablauf mehr als <see cref="TechnischeFristen.LoginAnfragen"/> zurückliegt, endgültig löschen.</summary>
internal sealed record LoginAnfragenLoeschen(DateTimeOffset Jetzt) : ICommand, IModulCommand<KontenDbContext>;

internal sealed class LoginAnfragenLoeschenHandler(KontenDbContext db) : ICommandHandler<LoginAnfragenLoeschen>
{
    public async ValueTask<Unit> Handle(LoginAnfragenLoeschen command, CancellationToken cancellationToken)
    {
        var grenze = command.Jetzt - TechnischeFristen.LoginAnfragen;
        await db.LoginAnfragen.Where(a => a.GueltigBis < grenze).ExecuteDeleteAsync(cancellationToken);
        return Unit.Value;
    }
}
