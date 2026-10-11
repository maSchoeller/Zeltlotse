using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Zugangslinks;
using OrgRolleArt = Zeltlotse.SharedKernel.Rechte.OrgRolle;

namespace Zeltlotse.Organisationen.Domaene;

/// <summary>Status einer <see cref="OrgEinladung"/>; gespeichert als Zahl, nur anhängen.</summary>
internal enum OrgEinladungStatus : short
{
    Offen = 1,
    Angenommen = 2,
    Verworfen = 3,
}

/// <summary>
/// Org-Einladung (AD-11): einmal annehmbar, 7 Tage gültig, gebunden an die Mail-Adresse. Gespeichert ist nur der Hash des
/// Tokens mit dem Zweck <see cref="Zweck"/>. Der Status ändert sich nur über die Methoden (AD-28).
/// </summary>
internal sealed class OrgEinladung : IOrganisationsEigen
{
    /// <summary>Zweck im Token-Hash; ein Token eines anderen Zwecks (z. B. Login) passt nie.</summary>
    public const string Zweck = "org-einladung";

    /// <summary>Gültigkeit ab dem Anlegen.</summary>
    public static readonly TimeSpan Gueltigkeit = TimeSpan.FromDays(7);

    private OrgEinladung()
    {
    }

    public Guid Id { get; private init; }

    public Guid OrganisationId { get; private init; }

    /// <summary>Normalisierte Mail-Adresse, an die die Einladung ging und für die sie gilt.</summary>
    public string Mail { get; private init; } = "";

    public OrgRolleArt Rolle { get; private init; }

    public byte[] TokenHash { get; private init; } = [];

    public DateTimeOffset AngelegtAm { get; private init; }

    public DateTimeOffset GueltigBis { get; private init; }

    /// <summary>Die einladende Person.</summary>
    public Guid EingeladenVon { get; private init; }

    public OrgEinladungStatus Status { get; private set; }

    /// <summary>Versionsprüfung (xmin): Zwei gleichzeitige Annahmen derselben Einladung gelingen nicht beide.</summary>
    public uint Version { get; private set; }

    public static OrgEinladung Anlegen(
        Guid id, Guid organisationId, string normalisierteMail, OrgRolleArt rolle, byte[] tokenHash, Guid eingeladenVon, DateTimeOffset jetzt) => new()
    {
        Id = id,
        OrganisationId = organisationId,
        Mail = normalisierteMail,
        Rolle = rolle,
        TokenHash = tokenHash,
        EingeladenVon = eingeladenVon,
        AngelegtAm = jetzt,
        GueltigBis = jetzt + Gueltigkeit,
        Status = OrgEinladungStatus.Offen,
    };

    /// <summary>Offen und noch nicht abgelaufen.</summary>
    public bool IstAnnehmbar(DateTimeOffset jetzt) => Status == OrgEinladungStatus.Offen && jetzt < GueltigBis;

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

        Status = OrgEinladungStatus.Angenommen;
    }

    /// <summary>Eine neuere Einladung derselben Mail-Adresse für dieselbe Organisation und Rolle verwirft diese.</summary>
    public void Verwerfen()
    {
        if (Status == OrgEinladungStatus.Offen)
        {
            Status = OrgEinladungStatus.Verworfen;
        }
    }
}
