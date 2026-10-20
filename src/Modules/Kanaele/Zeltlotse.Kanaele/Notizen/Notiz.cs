using Zeltlotse.Api.Contracts.Kanaele;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Kanaele.Notizen;

/// <summary>
/// Story 6-3 (CAP-37, FR-63): Ein Punkt auf dem Ansagen-Zettel oder der Besprechungsliste einer Freizeit. Er besteht nur aus Text und wird abgehakt oder
/// wieder geöffnet; bearbeitet oder einzeln gelöscht wird er nie. Kein <c>Aggregat</c>: Er merkt weder Push noch Mail vor.
/// </summary>
/// <remarks>
/// Ohne Personen-Kennung (kein Autor, kein „abgehakt von“): Datenauskunft und <c>PersonGeloescht</c> betreffen ihn nicht. Als Arbeitsdaten der Freizeit
/// (AD-16) löscht ihn der Lösch-Lauf nach der Frist der Organisation.
/// </remarks>
internal sealed class Notiz : IOrganisationsEigen
{
    private Notiz()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    /// <summary>Reihenfolge des Speicherns (von PostgreSQL vergeben); ordnet Punkte mit demselben Zeitpunkt.</summary>
    public long Laufnummer { get; private set; }

    public Guid FreizeitId { get; private set; }

    public NotizArt Art { get; private set; }

    public string Text { get; private set; } = "";

    public DateTimeOffset AngelegtAm { get; private set; }

    /// <summary>Wann der Punkt abgehakt wurde; <c>null</c>, solange er offen ist.</summary>
    public DateTimeOffset? ErledigtAm { get; private set; }

    /// <summary>Ein neuer, offener Punkt.</summary>
    public static Notiz Anlegen(Guid id, Guid organisationId, Guid freizeitId, NotizArt art, string text, DateTimeOffset jetzt) => new()
    {
        Id = id,
        OrganisationId = organisationId,
        FreizeitId = freizeitId,
        Art = art,
        Text = text,
        AngelegtAm = jetzt,
    };

    /// <summary>Hakt den Punkt ab; ein schon abgehakter Punkt behält seinen ersten Zeitpunkt.</summary>
    public void Abhaken(DateTimeOffset jetzt) => ErledigtAm ??= jetzt;

    /// <summary>Öffnet den Punkt wieder; ein offener Punkt bleibt offen.</summary>
    public void Oeffnen() => ErledigtAm = null;

    /// <summary>Nie den Text ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(Notiz)}({Id})";
}
