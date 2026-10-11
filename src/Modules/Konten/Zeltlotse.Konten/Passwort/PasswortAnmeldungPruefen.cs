using Mediator;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Konten.Daten;
using Zeltlotse.Konten.Domaene;
using Zeltlotse.Konten.Sitzungen;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;

namespace Zeltlotse.Konten.Passwort;

/// <summary>
/// Prüft Mail-Adresse und Passwort. Unbekannte Mail, Konto ohne Passwort und falsches Passwort enden gleich:
/// <c>null</c> und <c>AnmeldungFehlgeschlagen</c> im Sicherheitsprotokoll; geprüft wird auch dann gegen einen Prüfwert,
/// damit die Antwortzeit nichts verrät. Ein Prüfwert in älterem Format wird erneuert (deshalb ein Command).
/// </summary>
internal sealed record PasswortAnmeldungPruefen(string? Mail, string? Passwort)
    : ICommand<SitzungsDaten?>, IModulCommand<KontenDbContext>, IBenutzerCommand
{
    public override string ToString() => nameof(PasswortAnmeldungPruefen);
}

internal sealed class PasswortAnmeldungPruefenHandler(KontenDbContext db, IPasswordHasher<Konto> hasher, ISicherheitsprotokoll protokoll)
    : ICommandHandler<PasswortAnmeldungPruefen, SitzungsDaten?>
{
    private static readonly Lazy<string> Platzhalter = new(() => PasswortRegel.NeuerHasher().HashPassword(null!, Guid.NewGuid().ToString("N")));

    public async ValueTask<SitzungsDaten?> Handle(PasswortAnmeldungPruefen command, CancellationToken cancellationToken)
    {
        var passwort = command.Passwort ?? "";
        Konto? konto = null;
        if (!string.IsNullOrWhiteSpace(command.Mail) && command.Mail.Length <= MailAdresse.MaximaleLaenge && passwort.Length <= PasswortRegel.HoechstLaenge)
        {
            var mail = MailAdresse.Normalisieren(command.Mail);
            konto = await db.Konten.SingleOrDefaultAsync(k => k.Mail == mail, cancellationToken);
        }

        var ergebnis = konto?.PasswortHash is { } hash
            ? hasher.VerifyHashedPassword(konto, hash, passwort)
            : FalschMitGleicherDauer(passwort);

        if (ergebnis == PasswordVerificationResult.Failed)
        {
            await protokoll.SchreibenAsync(SicherheitsEreignisArt.AnmeldungFehlgeschlagen, konto?.PersonId, abbruch: cancellationToken);
            return null;
        }

        if (ergebnis == PasswordVerificationResult.SuccessRehashNeeded)
        {
            konto!.PasswortHashErneuern(hasher.HashPassword(konto, passwort));
        }

        return new SitzungsDaten(konto!.Id, konto.PersonId, konto.SitzungsStempel);
    }

    private PasswordVerificationResult FalschMitGleicherDauer(string passwort)
    {
        _ = hasher.VerifyHashedPassword(null!, Platzhalter.Value, passwort);
        return PasswordVerificationResult.Failed;
    }
}
