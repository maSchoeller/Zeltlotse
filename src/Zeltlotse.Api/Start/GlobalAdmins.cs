using Mediator;
using Zeltlotse.Api.Demo;
using Zeltlotse.Konten.Anmelden;
using Zeltlotse.SharedKernel.Zugangslinks;

namespace Zeltlotse.Api.Start;

/// <summary>
/// Global Admins aus der Konfiguration (FR-5): Die Liste <see cref="Schluessel"/> ist die Quelle, der Abgleich beim Start
/// schreibt sie nach den Migrationen und den Demo-Daten in das Kennzeichen der Konten. Bei aktiven Demo-Daten kommt die
/// Demo-Person mit Rolle <see cref="DemoRolle.GlobalAdmin"/> dazu. Eine leere Liste ist erlaubt.
/// </summary>
internal sealed partial class GlobalAdmins(IConfiguration konfiguration, IServiceScopeFactory scopes, ILogger<GlobalAdmins> logger)
{
    public const string Schluessel = "Zeltlotse:GlobalAdmins";

    /// <summary>Die Einträge der Konfiguration, ungeprüft (leere Einträge bleiben drin, damit die Startprüfung sie meldet).</summary>
    public static IReadOnlyList<string?> Eintraege(IConfiguration konfiguration) =>
        [.. konfiguration.GetSection(Schluessel).GetChildren().Select(c => c.Value)];

    /// <summary>Startprüfung: Jeder Eintrag muss eine gültige Mail-Adresse sein; sonst Abbruch mit Nennung des Schlüssels.</summary>
    public static void Pruefen(IConfiguration konfiguration)
    {
        if (!string.IsNullOrEmpty(konfiguration.GetSection(Schluessel).Value))
        {
            // Ein Einzelwert statt einer Liste (z. B. Zeltlotse__GlobalAdmins=a@x.de) würde still alle Global Admins entfernen.
            // Ein leerer Wert gilt als leere Liste (Einstellungen der Kinder hinterlassen ihn teils am Abschnitt).
            throw new InvalidOperationException($"{Schluessel} muss eine Liste sein (Zeltlotse:GlobalAdmins:0, :1, …), kein Einzelwert.");
        }

        var ungueltig = Eintraege(konfiguration).Count(e => !MailAdresse.IstGueltig(e));
        if (ungueltig > 0)
        {
            // Die Adressen selbst nennt die Meldung nicht (AD-22).
            throw new InvalidOperationException($"{Schluessel} enthält {ungueltig} ungültige Mail-Adresse(n).");
        }
    }

    /// <summary>Die Mail-Adressen, die nach dem Abgleich Global Admin sind.</summary>
    public IReadOnlyCollection<string> Mails()
    {
        var mails = Eintraege(konfiguration).OfType<string>().Select(MailAdresse.Normalisieren);
        if (DemoDaten.IstAktiv(konfiguration))
        {
            mails = mails.Concat(DemoKatalog.Personen.Where(p => p.Rolle == DemoRolle.GlobalAdmin).Select(p => MailAdresse.Normalisieren(p.Mail!)));
        }

        return mails.ToHashSet(StringComparer.Ordinal);
    }

    public async Task AbgleichenAsync(CancellationToken abbruch = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var ergebnis = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new GlobalAdminsAbgleichen(Mails()), abbruch);
        Abgeglichen(ergebnis.GlobalAdmins, ergebnis.NeueKonten);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Global Admins abgeglichen ({AnzahlGlobalAdmins} Global Admins, {NeueKonten} neue Konten).")]
    private partial void Abgeglichen(int anzahlGlobalAdmins, int neueKonten);
}
