using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Konten.Daten;
using Zeltlotse.Konten.Domaene;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Konten.Anmelden;

/// <summary>Legt eine Login-Anfrage für eine Mail-Adresse an, ob es ein Konto gibt oder nicht (keine Konto-Aufzählung).</summary>
internal sealed record LoginAnfrageAnlegen(string? Mail)
    : ICommand<NeueLoginAnfrage>, IModulCommand<KontenDbContext>, IBenutzerCommand
{
    /// <summary>Nie die Mail-Adresse ausgeben (AD-22).</summary>
    public override string ToString() => nameof(LoginAnfrageAnlegen);
}

/// <summary>Token und Code im Klartext gibt es nur hier, für die Login-Mail; gespeichert sind nur die Hashes.</summary>
internal sealed record NeueLoginAnfrage(Guid AnfrageId, string Mail, string Token, string Code)
{
    /// <summary>Nie Mail-Adresse, Token oder Code ausgeben (AD-22).</summary>
    public override string ToString() => $"NeueLoginAnfrage({AnfrageId})";
}

internal sealed partial class LoginAnfrageAnlegenValidator : AbstractValidator<LoginAnfrageAnlegen>
{
    public LoginAnfrageAnlegenValidator()
    {
        RuleFor(c => c.Mail)
            .Must(m => !string.IsNullOrWhiteSpace(m)).WithMessage("Mail-Adresse fehlt.")
            .DependentRules(() => RuleFor(c => c.Mail!.Trim())
                .MaximumLength(MailAdresse.MaximaleLaenge).WithMessage("Die Mail-Adresse ist zu lang.")
                .Must(IstReineMailAdresse).WithMessage("Das ist keine gültige Mail-Adresse.")
                .OverridePropertyName(nameof(LoginAnfrageAnlegen.Mail)));
    }
}

internal sealed partial class LoginAnfrageAnlegenValidator
{
    /// <summary>
    /// Nur eine nackte Adresse: kein Anzeigename (<c>x&lt;andere@y.de&gt;</c>), keine Liste, keine Anführungszeichen, keine
    /// Leerzeichen. So geht die Mail genau an die gespeicherte Adresse.
    /// </summary>
    internal static bool IstReineMailAdresse(string mail) =>
        !mail.Any(z => char.IsWhiteSpace(z) || z is '"' or ',' or ';' or '<' or '>')
        && System.Net.Mail.MailAddress.TryCreate(mail, out var adresse)
        && string.IsNullOrEmpty(adresse.DisplayName)
        && string.Equals(adresse.Address, mail, StringComparison.Ordinal);
}

internal sealed class LoginAnfrageAnlegenHandler(KontenDbContext db, TimeProvider uhr)
    : ICommandHandler<LoginAnfrageAnlegen, NeueLoginAnfrage>
{
    public async ValueTask<NeueLoginAnfrage> Handle(LoginAnfrageAnlegen command, CancellationToken cancellationToken)
    {
        var mail = MailAdresse.Normalisieren(command.Mail!);
        var jetzt = uhr.GetUtcNow();

        var offene = await db.LoginAnfragen
            .Where(a => a.Mail == mail && a.Status == LoginAnfrageStatus.Offen)
            .ToListAsync(cancellationToken);
        foreach (var aeltere in offene)
        {
            aeltere.Verwerfen();
        }

        var id = Kennung.Neu(uhr);
        var token = Geheimnisse.NeuesToken();
        var code = Geheimnisse.NeuerCode();
        db.LoginAnfragen.Add(LoginAnfrage.Anlegen(
            id, mail, Geheimnisse.TokenHash(Geheimnisse.LoginZweck, token), Geheimnisse.CodeHash(id, code), jetzt));
        return new NeueLoginAnfrage(id, mail, token, code);
    }
}
