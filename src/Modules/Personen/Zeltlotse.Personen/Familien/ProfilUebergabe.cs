using Zeltlotse.Personen.Contracts.Events;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Zugangslinks;

namespace Zeltlotse.Personen.Familien;

/// <summary>Status einer <see cref="ProfilUebergabe"/>; gespeichert als Zahl, nur anhängen.</summary>
internal enum UebergabeStatus : short
{
    Offen = 1,
    Angenommen = 2,
    Verworfen = 3,
}

/// <summary>
/// Übergabe eines Kinderprofils an das volljährige Kind (FR-4, AD-7, AD-11): einmal annehmbar, 14 Tage gültig, gebunden an
/// die Mail-Adresse. Gespeichert ist nur der Hash des Tokens mit dem Zweck <see cref="Zweck"/>. Der Status ändert sich nur
/// über die Methoden (AD-28); das Annehmen legt <see cref="ProfilUebergabeAngenommen"/> ab, die Übergabe ist der Datensatz im
/// Umschlag.
/// </summary>
internal sealed class ProfilUebergabe : Aggregat
{
    /// <summary>Zweck im Token-Hash; ein Token eines anderen Zwecks (Login, Einladungen) passt nie.</summary>
    public const string Zweck = "profil-uebergabe";

    /// <summary>Gültigkeit ab dem Auslösen.</summary>
    public static readonly TimeSpan Gueltigkeit = TimeSpan.FromDays(14);

    private ProfilUebergabe()
    {
    }

    public Guid FamilieId { get; private init; }

    /// <summary>Das Kinderprofil, das übergeben wird.</summary>
    public Guid PersonId { get; private init; }

    /// <summary>Normalisierte Mail-Adresse, an die die Übergabe ging und für die sie gilt.</summary>
    public string Mail { get; private init; } = "";

    public byte[] TokenHash { get; private init; } = [];

    public DateTimeOffset AngelegtAm { get; private init; }

    public DateTimeOffset GueltigBis { get; private init; }

    /// <summary>Der auslösende Elternteil.</summary>
    public Guid EingeladenVon { get; private init; }

    public UebergabeStatus Status { get; private set; }

    /// <summary>Versionsprüfung (xmin).</summary>
    public uint Version { get; private set; }

    public static ProfilUebergabe Ausloesen(
        Guid id, Guid familieId, Guid personId, string normalisierteMail, byte[] tokenHash, Guid eingeladenVon, DateTimeOffset jetzt) => new()
    {
        Id = id,
        FamilieId = familieId,
        PersonId = personId,
        Mail = normalisierteMail,
        TokenHash = tokenHash,
        EingeladenVon = eingeladenVon,
        AngelegtAm = jetzt,
        GueltigBis = jetzt + Gueltigkeit,
        Status = UebergabeStatus.Offen,
    };

    /// <summary>Offen und noch nicht abgelaufen.</summary>
    public bool IstAnnehmbar(DateTimeOffset jetzt) => Status == UebergabeStatus.Offen && jetzt < GueltigBis;

    /// <summary>Ob die Mail-Adresse eines Kontos die der Übergabe ist (Groß- und Kleinschreibung zählen nicht).</summary>
    public bool GiltFuer(string? kontoMail) =>
        kontoMail is not null && string.Equals(MailAdresse.Normalisieren(kontoMail), Mail, StringComparison.Ordinal);

    /// <summary>Nimmt die Übergabe für das Konto an und legt das Ereignis ab; danach ist sie verbraucht.</summary>
    public void Annehmen(Guid kontoId, DateTimeOffset jetzt)
    {
        if (!IstAnnehmbar(jetzt))
        {
            throw new InvalidOperationException("Die Übergabe ist nicht mehr annehmbar.");
        }

        Status = UebergabeStatus.Angenommen;
        EreignisAblegen(new ProfilUebergabeAngenommen(PersonId, kontoId));
    }

    /// <summary>Eine neuere Übergabe desselben Kindes verwirft diese.</summary>
    public void Verwerfen()
    {
        if (Status == UebergabeStatus.Offen)
        {
            Status = UebergabeStatus.Verworfen;
        }
    }
}
