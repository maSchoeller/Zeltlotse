using Zeltlotse.Api.Contracts.Sicherheit;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.Sicherheit.Contracts.Events;

namespace Zeltlotse.Sicherheit.Vorfaelle;

/// <summary>
/// Story 2-10: Ein Eintrag im Vorfall-Protokoll einer Person der Freizeit (CAP-17, FR-31): was passiert ist, welches Medikament gegeben
/// oder was aus dem Erste-Hilfe-Kasten entnommen wurde. Die Leitung sieht ihn sofort als Vorfall-Notiz. Der Text liegt nur verschlüsselt mit
/// dem Schlüssel der betroffenen Person (AD-14). Unveränderlich (AD-19): Eine Korrektur ist ein neuer Eintrag; die Kennung kommt vom Gerät
/// (AD-18). Story 3-4: Eine Entnahme meldet <see cref="ErsteHilfeEntnommen"/> (AD-28), damit Inventar sie auf die Schadens- und Verlustliste setzt.
/// </summary>
/// <remarks>
/// Lösch-Klasse „aufbewahrungspflichtig für die Organisation“ (AD-16): Nach dem Abschluss der Freizeit archiviert (<see cref="ArchiviertAm"/>),
/// nach der Frist für Freizeit-Daten gelöscht. Bei <c>PersonGeloescht</c> bleibt die Zeile; die Kennungen der Person werden zum Platzhalter,
/// und der Text ist mit ihrem vernichteten Schlüssel nicht mehr lesbar.
/// </remarks>
internal sealed class VorfallEintrag : Aggregat, IOrganisationsEigen
{
    private VorfallEintrag()
    {
    }

    public Guid OrganisationId { get; private init; }

    public Guid FreizeitId { get; private init; }

    /// <summary>Die betroffene Person; nach ihrer Löschung der Platzhalter <see cref="GeloeschtePerson.Id"/>.</summary>
    public Guid PersonId { get; private init; }

    /// <summary>Wer den Eintrag geschrieben hat; nach dessen Löschung der Platzhalter <see cref="GeloeschtePerson.Id"/>.</summary>
    public Guid VerfasstVon { get; private init; }

    public VorfallArt Art { get; private init; }

    /// <summary>Der Text, verschlüsselt mit dem Schlüssel der betroffenen Person.</summary>
    [Besonders]
    public Geschuetzt Text { get; private init; } = null!;

    public DateTimeOffset ErfasstAm { get; private init; }

    /// <summary>Seit dem Abschluss der Freizeit liegt der Eintrag im Archiv der Organisation.</summary>
    public DateTimeOffset? ArchiviertAm { get; private set; }

    public static VorfallEintrag Anlegen(
        Guid id, Guid organisationId, Guid freizeitId, Guid personId, Guid verfasstVon, VorfallArt art, Geschuetzt text, DateTimeOffset jetzt)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.PersonId != personId)
        {
            throw new InvalidOperationException("Der Text ist nicht mit dem Schlüssel der betroffenen Person verschlüsselt.");
        }

        return new VorfallEintrag
        {
            Id = id,
            OrganisationId = organisationId,
            FreizeitId = freizeitId,
            PersonId = personId,
            VerfasstVon = verfasstVon,
            Art = art,
            Text = text,
            ErfasstAm = jetzt,
        };
    }

    /// <summary>
    /// Story 3-4: Legt bei einer Entnahme das Ereignis <see cref="ErsteHilfeEntnommen"/> ab (ohne Text und Person, AD-4); bei anderen Arten nichts.
    /// In die Outbox kommt es nur, wenn der Handler den frisch gespeicherten Eintrag an den DbContext hängt.
    /// </summary>
    public void ErsteHilfeMelden()
    {
        if (Art == VorfallArt.Entnahme)
        {
            EreignisAblegen(new ErsteHilfeEntnommen(Id, FreizeitId, ErfasstAm));
        }
    }

    /// <summary>Legt den Eintrag ins Archiv; ist er schon archiviert, bleibt der erste Zeitpunkt.</summary>
    public void Archivieren(DateTimeOffset jetzt) => ArchiviertAm ??= jetzt;
}
