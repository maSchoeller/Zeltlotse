namespace Zeltlotse.Konten.Domaene;

/// <summary>
/// Konto (AD-7): besitzt nur die Anmeldung und die Zugangsdaten. Die Person dahinter (Profil) besitzt Personen;
/// Konten vergibt die <see cref="PersonId"/> beim Anlegen. Nicht organisationseigen.
/// </summary>
internal sealed class Konto
{
    private Konto()
    {
    }

    public Guid Id { get; private init; }

    /// <summary>Normalisiert (getrimmt, klein) und eindeutig, siehe <see cref="SharedKernel.Zugangslinks.MailAdresse"/>.</summary>
    public string Mail { get; private init; } = "";

    public Guid PersonId { get; private init; }

    /// <summary>Prüfwert im Identity-Hashformat v3; <c>null</c>, solange kein Passwort festgelegt ist.</summary>
    public string? PasswortHash { get; private set; }

    /// <summary>Neu bei jedem Festlegen des Passworts; Sitzungen mit einem anderen Stempel gelten nicht mehr.</summary>
    public Guid SitzungsStempel { get; private set; }

    public DateTimeOffset AngelegtAm { get; private init; }

    /// <summary>
    /// Global Admin (Betreiber): legt Organisationen an und lädt den ersten Org-Admin ein. Quelle ist die Konfiguration
    /// <c>Zeltlotse:GlobalAdmins</c>; der Abgleich beim Start schreibt sie hierher (<see cref="Anmelden.GlobalAdminsAbgleichen"/>).
    /// </summary>
    public bool GlobalAdmin { get; private set; }

    public static Konto Anlegen(Guid id, string normalisierteMail, Guid personId, DateTimeOffset jetzt) => new()
    {
        Id = id,
        Mail = normalisierteMail,
        PersonId = personId,
        SitzungsStempel = Guid.NewGuid(),
        AngelegtAm = jetzt,
    };

    /// <summary>Setzt oder entfernt das Global-Admin-Kennzeichen.</summary>
    public void GlobalAdminSetzen(bool globalAdmin) => GlobalAdmin = globalAdmin;

    /// <summary>Legt das Passwort fest; alle bisherigen Sitzungen werden ungültig.</summary>
    public void PasswortFestlegen(string passwortHash)
    {
        PasswortHash = passwortHash;
        SitzungsStempel = Guid.NewGuid();
    }

    /// <summary>Ersetzt den Prüfwert nach einem älteren Hashformat; die Sitzungen bleiben gültig.</summary>
    public void PasswortHashErneuern(string passwortHash)
    {
        if (PasswortHash is null)
        {
            throw new InvalidOperationException("Ohne Passwort gibt es keinen Prüfwert zu erneuern.");
        }

        PasswortHash = passwortHash;
    }
}
