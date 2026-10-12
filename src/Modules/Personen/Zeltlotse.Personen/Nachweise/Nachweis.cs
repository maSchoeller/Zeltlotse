using System.Globalization;
using Zeltlotse.Api.Contracts.Personen;
using NachweisArt = Zeltlotse.Personen.Contracts.Events.NachweisArt;
using Zeltlotse.SharedKernel.Datenschutz;

namespace Zeltlotse.Personen.Nachweise;

/// <summary>
/// Ein Nachweis am Profil einer Person (FR-8, AD-9): Art, Ausstellungsdatum und die verschlüsselte Datei im Dateispeicher
/// (Präfix <see cref="DateiPraefix"/>, Kennung = <see cref="Id"/>). Je Person und Art höchstens einer. Nicht organisationseigen:
/// Der Nachweis gehört der Person; Organisationen bekommen nur die Bestätigung. <see cref="HochgeladenAm"/> setzt nur der
/// Upload; eine neue Datei oder ein geändertes Datum löscht den Ablehnungshinweis.
/// </summary>
internal sealed class Nachweis
{
    /// <summary>Präfix der Dateien im Dateispeicher: das Schema des Moduls (AD-15).</summary>
    public const string DateiPraefix = PersonenModul.Schema;

    private Nachweis()
    {
    }

    public Guid Id { get; private init; }

    public Guid PersonId { get; private init; }

    public NachweisArt Art { get; private init; }

    public DateOnly Ausstellungsdatum { get; private set; }

    /// <summary>Zeitpunkt des letzten Datei-Uploads; <c>null</c>, solange es keine Datei gibt.</summary>
    public DateTimeOffset? HochgeladenAm { get; private set; }

    /// <summary>Hinweis der prüfenden Person nach einer Ablehnung, verschlüsselt mit dem Schlüssel der Person (AD-14).</summary>
    [Besonders]
    public Geschuetzt? Ablehnungshinweis { get; private set; }

    /// <summary>Von PostgreSQL bei jeder Änderung weitergezählt (<c>xmin</c>, AD-19).</summary>
    public uint Version { get; private set; }

    public static Nachweis Anlegen(Guid id, Guid personId, NachweisArt art, DateOnly ausstellungsdatum) => new()
    {
        Id = id,
        PersonId = personId,
        Art = art,
        Ausstellungsdatum = ausstellungsdatum,
    };

    /// <summary>Wert eines Felds als Text; das Datum im <see cref="NachweisFelder.Datumsformat"/>.</summary>
    public string? Wert(NachweisFeld feld) => feld switch
    {
        NachweisFeld.Ausstellungsdatum => Ausstellungsdatum.ToString(NachweisFelder.Datumsformat, CultureInfo.InvariantCulture),
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };

    /// <summary>
    /// Ändert ein Feld nach der Konfliktregel aus AD-19: Weicht der aktuelle Wert vom mitgeschickten alten Wert ab, bleibt alles,
    /// wie es ist (<c>false</c>). Ein geändertes Datum löscht den Ablehnungshinweis. Die Formprüfung hat der Validator erledigt.
    /// </summary>
    public bool FeldAendern(NachweisFeld feld, string? alterWert, string neuerWert)
    {
        if (!string.Equals(Wert(feld), alterWert?.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        switch (feld)
        {
            case NachweisFeld.Ausstellungsdatum:
                var datum = DateOnly.ParseExact(neuerWert.Trim(), NachweisFelder.Datumsformat, CultureInfo.InvariantCulture);
                if (datum != Ausstellungsdatum)
                {
                    Ausstellungsdatum = datum;
                    Ablehnungshinweis = null;
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(feld), feld, null);
        }

        return true;
    }

    /// <summary>Eine neue Datei liegt im Dateispeicher (ersetzt die alte); der Ablehnungshinweis gilt nicht mehr.</summary>
    public void DateiHochgeladen(DateTimeOffset jetzt)
    {
        HochgeladenAm = jetzt;
        Ablehnungshinweis = null;
    }

    /// <summary>Die prüfende Person hat abgelehnt; der Hinweis ist schon mit dem Schlüssel der Person verschlüsselt.</summary>
    public void Abgelehnt(Geschuetzt hinweis)
    {
        ArgumentNullException.ThrowIfNull(hinweis);
        if (hinweis.PersonId != PersonId)
        {
            throw new InvalidOperationException("Der Hinweis ist nicht mit dem Schlüssel der Person verschlüsselt.");
        }

        Ablehnungshinweis = hinweis;
    }
}
