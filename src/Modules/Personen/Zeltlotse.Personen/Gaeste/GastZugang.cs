using Zeltlotse.Personen.Contracts.Events;
using Zeltlotse.SharedKernel;

namespace Zeltlotse.Personen.Gaeste;

/// <summary>
/// Gast-Link einer Gast-Familie (AD-7, AD-11, FR-17): Elternteil ohne Konto, mehrfach nutzbar, nur als Hash gespeichert (Zweck
/// <see cref="Zweck"/>). Er gilt für die ganze Familie, solange eine Freigabe einer ihrer Personen läuft; vor der ersten Freigabe
/// <see cref="VorDerFreigabe"/> ab dem Anlegen. Nach der Übernahme durch ein Konto gilt er nicht mehr. Gehört der Person
/// (personeneigen, AD-16). Aggregat (AD-28): Die Übernahme legt <see cref="ProfilUebergabeAngenommen"/> ab, damit Konten das
/// Konto mit dem Profil des Elternteils verbindet; der Zugang ist der Datensatz im Umschlag.
/// </summary>
internal sealed class GastZugang : Aggregat
{
    /// <summary>Zweck im Token-Hash; ein Token eines anderen Zwecks (Anmeldelink, Login, Einladungen) passt nie.</summary>
    public const string Zweck = "gast-link";

    /// <summary>Gültigkeit vor der ersten Freigabe einer Person der Familie.</summary>
    public static readonly TimeSpan VorDerFreigabe = TimeSpan.FromDays(1);

    private GastZugang()
    {
    }

    /// <summary>Der Elternteil ohne Konto; über ihn ist die Familie bestimmt.</summary>
    public Guid ElternPersonId { get; private init; }

    /// <summary>Normalisierte Mail-Adresse des Elternteils (Eingangsmail).</summary>
    public string Mail { get; private init; } = "";

    public byte[] TokenHash { get; private init; } = [];

    public DateTimeOffset AngelegtAm { get; private init; }

    /// <summary>Wann ein Konto die Angaben übernommen hat; danach ist der Gast-Link ungültig.</summary>
    public DateTimeOffset? UebernommenAm { get; private set; }

    public static GastZugang Anlegen(Guid id, Guid elternPersonId, string normalisierteMail, byte[] tokenHash, DateTimeOffset jetzt) => new()
    {
        Id = id,
        ElternPersonId = elternPersonId,
        Mail = normalisierteMail,
        TokenHash = tokenHash,
        AngelegtAm = jetzt,
    };

    /// <summary>
    /// Bis wann der Gast-Link gilt: das späteste „gültig bis“ der Freigaben der Familie; ohne Freigabe <see cref="VorDerFreigabe"/>
    /// ab dem Anlegen.
    /// </summary>
    public DateTimeOffset GueltigBis(IEnumerable<DateTimeOffset> freigabenGueltigBis)
    {
        var freigaben = freigabenGueltigBis.ToList();
        return freigaben.Count > 0 ? freigaben.Max() : AngelegtAm + VorDerFreigabe;
    }

    /// <summary>Nicht übernommen und noch nicht abgelaufen.</summary>
    public bool IstGueltig(DateTimeOffset jetzt, IEnumerable<DateTimeOffset> freigabenGueltigBis) =>
        UebernommenAm is null && jetzt < GueltigBis(freigabenGueltigBis);

    /// <summary>Ein Konto übernimmt die Angaben; legt das Ereignis ab. Danach gilt der Gast-Link nicht mehr.</summary>
    public void Uebernehmen(Guid kontoId, DateTimeOffset jetzt)
    {
        if (UebernommenAm is not null)
        {
            throw new InvalidOperationException("Der Gast-Link ist schon übernommen.");
        }

        UebernommenAm = jetzt;
        EreignisAblegen(new ProfilUebergabeAngenommen(ElternPersonId, kontoId));
    }
}
