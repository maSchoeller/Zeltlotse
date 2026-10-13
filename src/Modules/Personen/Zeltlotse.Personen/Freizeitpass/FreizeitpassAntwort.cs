using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.SharedKernel.Datenschutz;

namespace Zeltlotse.Personen.Freizeitpass;

/// <summary>
/// Eine Antwort im Freizeitpass (FR-23, AD-8, AD-10): je (Person, Kennung) eine Zeile. Sie gehört der Person, nicht einer Freizeit;
/// darum steht sie bei der nächsten Freizeit schon da. Wert und „Sonstiges“ liegen nur verschlüsselt mit dem Schlüssel der Person
/// (AD-14). Jede Wertänderung und jedes „Stimmt“ setzt <see cref="BestaetigtAm"/> neu; älter als 12 Monate heißt „Stimmt das noch?“.
/// Lösch-Klasse personeneigen (AD-16).
/// </summary>
internal sealed class FreizeitpassAntwort
{
    private FreizeitpassAntwort()
    {
    }

    public Guid Id { get; private init; }

    public Guid PersonId { get; private init; }

    /// <summary>Kennung der Katalogfrage.</summary>
    public string Kennung { get; private init; } = "";

    /// <summary>Wert im Wire-Format, verschlüsselt; <c>null</c> = keine Angabe.</summary>
    [Besonders]
    public Geschuetzt? Wert { get; private set; }

    /// <summary>Freitext „Sonstiges“, verschlüsselt; <c>null</c> = keine Angabe.</summary>
    [Besonders]
    public Geschuetzt? Sonstiges { get; private set; }

    /// <summary>Letzte Änderung oder Bestätigung („Stimmt“).</summary>
    public DateTimeOffset BestaetigtAm { get; private set; }

    /// <summary>Von PostgreSQL bei jeder Änderung weitergezählt (<c>xmin</c>, AD-19).</summary>
    public uint Version { get; private set; }

    /// <summary>Wert oder „Sonstiges“ ist angegeben.</summary>
    public bool Beantwortet => Wert is not null || Sonstiges is not null;

    public static FreizeitpassAntwort Anlegen(Guid id, Guid personId, string kennung, DateTimeOffset jetzt) => new()
    {
        Id = id,
        PersonId = personId,
        Kennung = kennung,
        BestaetigtAm = jetzt,
    };

    /// <summary>Beantwortet und zuletzt vor mehr als 12 Monaten bestätigt (UTC).</summary>
    public static bool IstVeraltet(bool beantwortet, DateTimeOffset bestaetigtAm, DateTimeOffset jetzt) =>
        beantwortet && bestaetigtAm < jetzt.ToUniversalTime().AddMonths(-12);

    /// <summary>Setzt ein Feld (schon verschlüsselt, mit dem Schlüssel dieser Person) und bestätigt die Antwort.</summary>
    public void Setzen(FreizeitpassFeld feld, Geschuetzt? neu, DateTimeOffset jetzt)
    {
        if (neu is not null && neu.PersonId != PersonId)
        {
            throw new InvalidOperationException("Der Wert ist nicht mit dem Schlüssel der Person verschlüsselt.");
        }

        switch (feld)
        {
            case FreizeitpassFeld.Wert:
                Wert = neu;
                break;
            case FreizeitpassFeld.Sonstiges:
                Sonstiges = neu;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(feld), feld, null);
        }

        BestaetigtAm = jetzt;
    }

    /// <summary>„Stimmt“: die Angabe gilt weiter.</summary>
    public void Bestaetigen(DateTimeOffset jetzt) => BestaetigtAm = jetzt;
}
