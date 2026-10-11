namespace Zeltlotse.Konten.Domaene;

/// <summary>Status einer <see cref="LoginAnfrage"/>; gespeichert als Zahl, nur anhängen.</summary>
internal enum LoginAnfrageStatus : short
{
    Offen = 1,
    Eingeloest = 2,
    Verworfen = 3,
}

/// <summary>
/// Login-Link und Login-Code einer Mail-Adresse (AD-11): einmal einlösbar, 15 Minuten gültig, gebunden an die Mail-Adresse
/// und damit an deren Konto. Gespeichert werden nur die Hashes von Token und Code. Der Status ändert sich nur über
/// die Methoden (AD-28).
/// </summary>
internal sealed class LoginAnfrage
{
    /// <summary>Gültigkeit ab dem Anlegen.</summary>
    public static readonly TimeSpan Gueltigkeit = TimeSpan.FromMinutes(15);

    /// <summary>Nach so vielen falschen Codes ist die Anfrage verworfen.</summary>
    public const int MaximaleFehlversuche = 5;

    private LoginAnfrage()
    {
    }

    public Guid Id { get; private init; }

    /// <summary>Normalisierte Mail-Adresse, für die Link und Code gelten.</summary>
    public string Mail { get; private init; } = "";

    public byte[] TokenHash { get; private init; } = [];

    public byte[] CodeHash { get; private init; } = [];

    public DateTimeOffset AngelegtAm { get; private init; }

    public DateTimeOffset GueltigBis { get; private init; }

    public int Fehlversuche { get; private set; }

    public LoginAnfrageStatus Status { get; private set; }

    /// <summary>Versionsprüfung (xmin): Zwei gleichzeitige Einlösungen derselben Anfrage gelingen nicht beide.</summary>
    public uint Version { get; private set; }

    public static LoginAnfrage Anlegen(Guid id, string normalisierteMail, byte[] tokenHash, byte[] codeHash, DateTimeOffset jetzt) => new()
    {
        Id = id,
        Mail = normalisierteMail,
        TokenHash = tokenHash,
        CodeHash = codeHash,
        AngelegtAm = jetzt,
        GueltigBis = jetzt + Gueltigkeit,
        Status = LoginAnfrageStatus.Offen,
    };

    /// <summary>Offen und noch nicht abgelaufen.</summary>
    public bool IstEinloesbar(DateTimeOffset jetzt) => Status == LoginAnfrageStatus.Offen && jetzt < GueltigBis;

    /// <summary>Löst die Anfrage ein (Link oder Code); danach ist sie verbraucht.</summary>
    public void Einloesen(DateTimeOffset jetzt)
    {
        if (!IstEinloesbar(jetzt))
        {
            throw new InvalidOperationException("Die Login-Anfrage ist nicht mehr einlösbar.");
        }

        Status = LoginAnfrageStatus.Eingeloest;
    }

    /// <summary>Ein falscher Code; beim fünften ist die Anfrage verworfen.</summary>
    public void FehlversuchZaehlen()
    {
        if (Status != LoginAnfrageStatus.Offen)
        {
            return;
        }

        Fehlversuche++;
        if (Fehlversuche >= MaximaleFehlversuche)
        {
            Status = LoginAnfrageStatus.Verworfen;
        }
    }

    /// <summary>Eine neuere Anfrage derselben Mail-Adresse verwirft diese.</summary>
    public void Verwerfen()
    {
        if (Status == LoginAnfrageStatus.Offen)
        {
            Status = LoginAnfrageStatus.Verworfen;
        }
    }
}
