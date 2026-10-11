using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Konten.Daten;
using Zeltlotse.Konten.Domaene;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Zugangslinks;

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

internal sealed class LoginAnfrageAnlegenValidator : AbstractValidator<LoginAnfrageAnlegen>
{
    public LoginAnfrageAnlegenValidator()
    {
        RuleFor(c => c.Mail)
            .Must(m => !string.IsNullOrWhiteSpace(m)).WithMessage("Mail-Adresse fehlt.")
            .DependentRules(() => RuleFor(c => c.Mail!.Trim())
                .MaximumLength(MailAdresse.MaximaleLaenge).WithMessage("Die Mail-Adresse ist zu lang.")
                .Must(MailAdresse.IstReineMailAdresse).WithMessage("Das ist keine gültige Mail-Adresse.")
                .OverridePropertyName(nameof(LoginAnfrageAnlegen.Mail)));
    }
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
        var token = Zugangslink.NeuesToken();
        var code = Geheimnisse.NeuerCode();
        db.LoginAnfragen.Add(LoginAnfrage.Anlegen(
            id, mail, Zugangslink.TokenHash(Geheimnisse.LoginZweck, token), Geheimnisse.CodeHash(id, code), jetzt));
        return new NeueLoginAnfrage(id, mail, token, code);
    }
}
