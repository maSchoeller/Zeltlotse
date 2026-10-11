using FluentValidation;

namespace Zeltlotse.Organisationen.Einladungen;

/// <summary>Formprüfung einer Mail-Adresse wie bei der Anmeldung: Pflicht, höchstens 254 Zeichen, nur eine nackte Adresse.</summary>
internal static class MailRegel
{
    public static IRuleBuilderOptions<T, string?> MailAdresse<T>(this IRuleBuilder<T, string?> regel) =>
        regel
            .Must(m => !string.IsNullOrWhiteSpace(m)).WithMessage("Mail-Adresse fehlt.")
            .Must(m => string.IsNullOrWhiteSpace(m) || m.Trim().Length <= SharedKernel.Zugangslinks.MailAdresse.MaximaleLaenge)
            .WithMessage("Die Mail-Adresse ist zu lang.")
            .Must(m => string.IsNullOrWhiteSpace(m) || m.Trim().Length > SharedKernel.Zugangslinks.MailAdresse.MaximaleLaenge
                       || SharedKernel.Zugangslinks.MailAdresse.IstReineMailAdresse(m.Trim()))
            .WithMessage("Das ist keine gültige Mail-Adresse.");
}
