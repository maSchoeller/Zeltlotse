using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Zeltlotse.SharedKernel.Mail;

namespace Zeltlotse.Api.Adapter;

/// <summary>Art des Mail-Adapters, Schlüssel <c>Zeltlotse:Mail:Art</c>.</summary>
internal enum MailArt
{
    /// <summary>Versand über einen SMTP-Server (Betrieb, lokal Mailpit).</summary>
    Smtp,

    /// <summary>Schreibt jede Mail als <c>.eml</c>-Datei in einen Ordner; nur in <c>Development</c> und <c>Testing</c>.</summary>
    Ordner,
}

/// <summary>Einstellungen des Mail-Versands, Abschnitt <c>Zeltlotse:Mail</c> (Zugangsdaten aus der Geheimnisverwaltung).</summary>
internal sealed class MailOptionen
{
    public const string Abschnitt = "Zeltlotse:Mail";

    public const string ArtSchluessel = Abschnitt + ":Art";

    public const string OrdnerSchluessel = Abschnitt + ":Ordner";

    public const string AbsenderSchluessel = Abschnitt + ":Absender";

    public const string SmtpHostSchluessel = Abschnitt + ":Smtp:Host";

    /// <summary>Absender, wenn nichts konfiguriert ist (nur für den Ordner-Adapter).</summary>
    public const string VorgabeAbsender = "Zeltlotse <zeltlotse@example.org>";

    public MailArt? Art { get; set; }

    public string? Ordner { get; set; }

    public string? Absender { get; set; }

    public SmtpOptionen Smtp { get; set; } = new();
}

internal sealed class SmtpOptionen
{
    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    public string? Benutzer { get; set; }

    public string? Passwort { get; set; }

    /// <summary><c>Auto</c> (Vorgabe), <c>None</c>, <c>StartTls</c>, <c>SslOnConnect</c> …</summary>
    public SecureSocketOptions Verschluesselung { get; set; } = SecureSocketOptions.Auto;
}

/// <summary>Baut die Mail; Text ohne HTML, UTF-8.</summary>
internal static class MailNachricht
{
    public static MimeMessage Erstellen(string absender, string an, string betreff, string text)
    {
        var nachricht = new MimeMessage();
        nachricht.From.Add(MailboxAddress.Parse(absender));
        // Die Adresse ist geprüft und normalisiert; nicht als Text mit Anzeigename parsen.
        nachricht.To.Add(new MailboxAddress("", an));
        nachricht.Subject = betreff;
        nachricht.Body = new TextPart("plain") { Text = text };
        return nachricht;
    }
}

/// <summary>SMTP-Adapter für <see cref="IMailVersand"/> (AD-23): der einzige Ort mit SMTP-Code. Eine Verbindung je Mail.</summary>
internal sealed class SmtpMailVersand(IOptions<MailOptionen> optionen) : IMailVersand
{
    public async Task SendenAsync(string an, string betreff, string text, CancellationToken abbruch = default)
    {
        var o = optionen.Value;
        using var nachricht = MailNachricht.Erstellen(o.Absender!, an, betreff, text);
        using var client = new SmtpClient();
        await client.ConnectAsync(o.Smtp.Host!, o.Smtp.Port, o.Smtp.Verschluesselung, abbruch);
        if (!string.IsNullOrEmpty(o.Smtp.Benutzer))
        {
            await client.AuthenticateAsync(o.Smtp.Benutzer, o.Smtp.Passwort ?? "", abbruch);
        }

        await client.SendAsync(nachricht, abbruch);
        await client.DisconnectAsync(quit: true, abbruch);
    }
}

/// <summary>
/// Schreibt jede Mail als <c>.eml</c> in <c>Zeltlotse:Mail:Ordner</c> (nur <c>Development</c> und <c>Testing</c>; die
/// Startprüfung bricht sonst ab). So lesen Tests und Entwicklung Login-Links ohne Mail-Server.
/// </summary>
internal sealed class OrdnerMailVersand(IOptions<MailOptionen> optionen, TimeProvider uhr) : IMailVersand
{
    public async Task SendenAsync(string an, string betreff, string text, CancellationToken abbruch = default)
    {
        var o = optionen.Value;
        Directory.CreateDirectory(o.Ordner!);
        using var nachricht = MailNachricht.Erstellen(o.Absender ?? MailOptionen.VorgabeAbsender, an, betreff, text);
        nachricht.Date = uhr.GetUtcNow();
        var datei = Path.Combine(o.Ordner!, $"{uhr.GetUtcNow():yyyyMMdd-HHmmss-fffffff}-{Guid.NewGuid():N}.eml");
        await nachricht.WriteToAsync(datei, abbruch);
    }
}

internal static class MailRegistrierung
{
    public static IServiceCollection AddMailVersand(this IServiceCollection services)
    {
        services.AddOptions<MailOptionen>().BindConfiguration(MailOptionen.Abschnitt);
        services.AddSingleton<IMailVersand>(sp =>
            sp.GetRequiredService<IOptions<MailOptionen>>().Value.Art switch
            {
                MailArt.Smtp => ActivatorUtilities.CreateInstance<SmtpMailVersand>(sp),
                MailArt.Ordner => ActivatorUtilities.CreateInstance<OrdnerMailVersand>(sp),
                _ => throw new InvalidOperationException($"{MailOptionen.ArtSchluessel} fehlt."),
            });
        return services;
    }

    /// <summary>
    /// Startprüfung: Art gesetzt (<c>Smtp</c> oder <c>Ordner</c>), <c>Ordner</c> nur in <c>Development</c>/<c>Testing</c> und mit
    /// Ordner, <c>Smtp</c> mit Host und lesbarem Absender. Die Meldung nennt den Schlüssel, nie einen Wert.
    /// </summary>
    public static void Pruefen(IConfiguration konfiguration, IHostEnvironment umgebung, string testUmgebung)
    {
        var art = konfiguration[MailOptionen.ArtSchluessel]?.Trim();
        if (string.IsNullOrEmpty(art) || !Enum.TryParse<MailArt>(art, ignoreCase: true, out var mailArt) || !Enum.IsDefined(mailArt))
        {
            throw new InvalidOperationException($"{MailOptionen.ArtSchluessel} fehlt oder ist weder Smtp noch Ordner.");
        }

        if (mailArt == MailArt.Ordner)
        {
            if (!umgebung.IsDevelopment() && !umgebung.IsEnvironment(testUmgebung))
            {
                throw new InvalidOperationException(
                    $"{MailOptionen.ArtSchluessel}=Ordner ist nur in den Umgebungen Development und {testUmgebung} erlaubt, nicht in {umgebung.EnvironmentName}.");
            }

            if (string.IsNullOrWhiteSpace(konfiguration[MailOptionen.OrdnerSchluessel]))
            {
                throw new InvalidOperationException($"{MailOptionen.OrdnerSchluessel} fehlt.");
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(konfiguration[MailOptionen.SmtpHostSchluessel]))
            {
                throw new InvalidOperationException($"{MailOptionen.SmtpHostSchluessel} fehlt.");
            }

            if (string.IsNullOrWhiteSpace(konfiguration[MailOptionen.AbsenderSchluessel]))
            {
                throw new InvalidOperationException($"{MailOptionen.AbsenderSchluessel} fehlt.");
            }

            if (!MailboxAddress.TryParse(konfiguration[MailOptionen.AbsenderSchluessel], out _))
            {
                throw new InvalidOperationException($"{MailOptionen.AbsenderSchluessel} ist keine lesbare Mail-Adresse.");
            }
        }
    }
}
