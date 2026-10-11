using Zeltlotse.SharedKernel.Zugangslinks;

namespace Zeltlotse.Personen.Familien;

/// <summary>Status einer <see cref="FamilienEinladung"/>; gespeichert als Zahl, nur anhängen.</summary>
internal enum FamilienEinladungStatus : short
{
    Offen = 1,
    Angenommen = 2,
    Verworfen = 3,
}

/// <summary>
/// Einladung eines zweiten Elternteils ins Familienkonto (AD-11, FR-3): einmal annehmbar, 7 Tage gültig, gebunden an die
/// Mail-Adresse. Gespeichert ist nur der Hash des Tokens mit dem Zweck <see cref="Zweck"/>. Der Status ändert sich nur über
/// die Methoden (AD-28).
/// </summary>
internal sealed class FamilienEinladung
{
    /// <summary>Zweck im Token-Hash; ein Token eines anderen Zwecks (Login, Org-Einladung, Übergabe) passt nie.</summary>
    public const string Zweck = "familien-einladung";

    /// <summary>Gültigkeit ab dem Anlegen.</summary>
    public static readonly TimeSpan Gueltigkeit = TimeSpan.FromDays(7);

    private FamilienEinladung()
    {
    }

    public Guid Id { get; private init; }

    public Guid FamilieId { get; private init; }

    /// <summary>Normalisierte Mail-Adresse, an die die Einladung ging und für die sie gilt.</summary>
    public string Mail { get; private init; } = "";

    public byte[] TokenHash { get; private init; } = [];

    public DateTimeOffset AngelegtAm { get; private init; }

    public DateTimeOffset GueltigBis { get; private init; }

    /// <summary>Der einladende Elternteil.</summary>
    public Guid EingeladenVon { get; private init; }

    public FamilienEinladungStatus Status { get; private set; }

    /// <summary>Versionsprüfung (xmin).</summary>
    public uint Version { get; private set; }

    public static FamilienEinladung Anlegen(
        Guid id, Guid familieId, string normalisierteMail, byte[] tokenHash, Guid eingeladenVon, DateTimeOffset jetzt) => new()
    {
        Id = id,
        FamilieId = familieId,
        Mail = normalisierteMail,
        TokenHash = tokenHash,
        EingeladenVon = eingeladenVon,
        AngelegtAm = jetzt,
        GueltigBis = jetzt + Gueltigkeit,
        Status = FamilienEinladungStatus.Offen,
    };

    /// <summary>Offen und noch nicht abgelaufen.</summary>
    public bool IstAnnehmbar(DateTimeOffset jetzt) => Status == FamilienEinladungStatus.Offen && jetzt < GueltigBis;

    /// <summary>Ob die Mail-Adresse eines Kontos die eingeladene ist (Groß- und Kleinschreibung zählen nicht).</summary>
    public bool GiltFuer(string? kontoMail) =>
        kontoMail is not null && string.Equals(MailAdresse.Normalisieren(kontoMail), Mail, StringComparison.Ordinal);

    /// <summary>Nimmt die Einladung an; danach ist sie verbraucht.</summary>
    public void Annehmen(DateTimeOffset jetzt)
    {
        if (!IstAnnehmbar(jetzt))
        {
            throw new InvalidOperationException("Die Einladung ist nicht mehr annehmbar.");
        }

        Status = FamilienEinladungStatus.Angenommen;
    }

    /// <summary>Eine neuere Einladung derselben Familie verwirft diese.</summary>
    public void Verwerfen()
    {
        if (Status == FamilienEinladungStatus.Offen)
        {
            Status = FamilienEinladungStatus.Verworfen;
        }
    }
}
