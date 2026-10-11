using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Zugangslinks;

namespace Zeltlotse.Freizeiten.Domaene;

/// <summary>Status einer <see cref="LeitungsEinladung"/>; gespeichert als Zahl, nur anhängen.</summary>
internal enum LeitungsEinladungStatus : short
{
    Offen = 1,
    Angenommen = 2,
    Verworfen = 3,
}

/// <summary>
/// Einladung als Leitung einer Freizeit (AD-11, FR-6): einmal annehmbar, 7 Tage gültig, gebunden an die Mail-Adresse.
/// Gespeichert ist nur der Hash des Tokens mit dem Zweck <see cref="Zweck"/>. Der Status ändert sich nur über die Methoden
/// (AD-28).
/// </summary>
internal sealed class LeitungsEinladung : IOrganisationsEigen
{
    /// <summary>Zweck im Token-Hash; ein Token eines anderen Zwecks (Login, Org-Einladung) passt nie.</summary>
    public const string Zweck = "leitung-einladung";

    /// <summary>Gültigkeit ab dem Anlegen.</summary>
    public static readonly TimeSpan Gueltigkeit = TimeSpan.FromDays(7);

    private LeitungsEinladung()
    {
    }

    public Guid Id { get; private init; }

    public Guid OrganisationId { get; private init; }

    public Guid FreizeitId { get; private init; }

    /// <summary>Normalisierte Mail-Adresse, an die die Einladung ging und für die sie gilt.</summary>
    public string Mail { get; private init; } = "";

    public byte[] TokenHash { get; private init; } = [];

    public DateTimeOffset AngelegtAm { get; private init; }

    public DateTimeOffset GueltigBis { get; private init; }

    /// <summary>Die einladende Person.</summary>
    public Guid EingeladenVon { get; private init; }

    public LeitungsEinladungStatus Status { get; private set; }

    /// <summary>Versionsprüfung (xmin).</summary>
    public uint Version { get; private set; }

    public static LeitungsEinladung Anlegen(
        Guid id, Guid organisationId, Guid freizeitId, string normalisierteMail, byte[] tokenHash, Guid eingeladenVon, DateTimeOffset jetzt) => new()
    {
        Id = id,
        OrganisationId = organisationId,
        FreizeitId = freizeitId,
        Mail = normalisierteMail,
        TokenHash = tokenHash,
        EingeladenVon = eingeladenVon,
        AngelegtAm = jetzt,
        GueltigBis = jetzt + Gueltigkeit,
        Status = LeitungsEinladungStatus.Offen,
    };

    /// <summary>Offen und noch nicht abgelaufen.</summary>
    public bool IstAnnehmbar(DateTimeOffset jetzt) => Status == LeitungsEinladungStatus.Offen && jetzt < GueltigBis;

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

        Status = LeitungsEinladungStatus.Angenommen;
    }

    /// <summary>Eine neuere Einladung derselben Mail-Adresse für dieselbe Freizeit verwirft diese.</summary>
    public void Verwerfen()
    {
        if (Status == LeitungsEinladungStatus.Offen)
        {
            Status = LeitungsEinladungStatus.Verworfen;
        }
    }
}
