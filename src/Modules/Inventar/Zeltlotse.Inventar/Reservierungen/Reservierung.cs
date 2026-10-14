using Zeltlotse.Inventar.Contracts.Events;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Inventar.Reservierungen;

/// <summary>
/// Story 3-3: Eine Reservierung eines Inventar-Artikels für eine Freizeit und einen Zeitraum (FR-37), beide Tage eingeschlossen; optional für ein
/// Material am Programmpunkt. Mehrere Reservierungen desselben Artikels in derselben Freizeit sind erlaubt; geändert wird nicht, sondern
/// aufgehoben und neu reserviert. Hängt ein Material daran, legen <see cref="Anlegen"/> und <see cref="Aufheben"/> das Ereignis
/// <see cref="ReservierungGeaendert"/> ab (AD-28): Programm setzt damit den Materialstatus. Kein Personenbezug.
/// </summary>
internal sealed class Reservierung : Aggregat, IOrganisationsEigen
{
    private Reservierung()
    {
    }

    public Guid OrganisationId { get; private init; }

    public Guid ArtikelId { get; private init; }

    public Guid FreizeitId { get; private init; }

    public DateOnly Von { get; private init; }

    public DateOnly Bis { get; private init; }

    /// <summary>Das Material am Programmpunkt (Kennung aus Programm, nicht geprüft), für das reserviert ist, sonst <c>null</c>.</summary>
    public Guid? MaterialId { get; private init; }

    public static Reservierung Anlegen(Guid id, Guid organisationId, Guid artikelId, Guid freizeitId, DateOnly von, DateOnly bis, Guid? materialId)
    {
        if (von > bis)
        {
            throw new ArgumentOutOfRangeException(nameof(bis), bis, "Bis liegt vor Von.");
        }

        var reservierung = new Reservierung
        {
            Id = id,
            OrganisationId = organisationId,
            ArtikelId = artikelId,
            FreizeitId = freizeitId,
            Von = von,
            Bis = bis,
            MaterialId = materialId,
        };
        reservierung.Melden(ReservierungArt.Reserviert);
        return reservierung;
    }

    /// <summary>Hebt die Reservierung auf; der Handler entfernt sie danach. Mit Material folgt das Ereignis „aufgehoben“.</summary>
    public void Aufheben() => Melden(ReservierungArt.Aufgehoben);

    /// <summary>Überschneiden sich zwei Zeiträume? Tage zählen einschließlich: der letzte Tag der einen und der erste der anderen gleich reicht.</summary>
    public static bool Ueberschneiden(DateOnly aVon, DateOnly aBis, DateOnly bVon, DateOnly bBis) => aVon <= bBis && bVon <= aBis;

    /// <summary>Ein Konflikt: derselbe Artikel, eine andere Freizeit, überschneidende Zeiträume.</summary>
    public bool KonfliktMit(Reservierung andere) =>
        andere.ArtikelId == ArtikelId && andere.FreizeitId != FreizeitId && Ueberschneiden(Von, Bis, andere.Von, andere.Bis);

    private void Melden(ReservierungArt art)
    {
        if (MaterialId is not null)
        {
            EreignisAblegen(new ReservierungGeaendert(Id, FreizeitId, ArtikelId, MaterialId, art));
        }
    }
}
