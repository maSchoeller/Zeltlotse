namespace Zeltlotse.SharedKernel.Mail;

/// <summary>
/// Mail-Versand (AD-13, AD-23). Der Adapter steht im API-Host; nur dort gibt es anbieterspezifischen Code.
/// Mail-Adresse, Betreff und Text erscheinen nie in Logs, Ausnahmen oder dem Sicherheitsprotokoll.
/// </summary>
public interface IMailVersand
{
    Task SendenAsync(string an, string betreff, string text, CancellationToken abbruch = default);
}
