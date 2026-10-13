using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.SharedKernel.Dateien;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Freizeiten.Dokumente;

/// <summary>Ergebnis von <see cref="Dokument.FeldAendern"/>.</summary>
internal enum DokumentFeldErgebnis
{
    Gespeichert,

    /// <summary>Der aktuelle Wert desselben Felds weicht vom mitgeschickten alten Wert ab (AD-19).</summary>
    Konflikt,

    /// <summary>Den Titel einer Einbindung pflegt die Organisation.</summary>
    NurOrganisation,
}

/// <summary>
/// Ein Dokument der Freizeit (Story 2-7, CAP-25, FR-43, AD-15): eine eigene Datei (Bild oder PDF, unverschlüsselt im Dateispeicher
/// unter <c>freizeiten/&lt;Id&gt;</c>) oder die Einbindung eines Org-Dokuments per Kennung (<see cref="OrgDokumentId"/>). Eine
/// Einbindung kopiert nichts: Titel, Art, Größe und Datei kommen bei jedem Lesen aus Organisationen; hier stehen dann nur Kennung und
/// Sichtbarkeit. Kein Personenbezug (kein Uploader), also keine Löschklasse. Die Sichtbarkeit legt fest, wer das Dokument sieht.
/// </summary>
internal sealed class Dokument : IOrganisationsEigen
{
    /// <summary>Präfix im Dateispeicher: das Schema des Moduls.</summary>
    public const string DateiPraefix = FreizeitenModul.Schema;

    private Dokument()
    {
    }

    public Guid Id { get; private init; }

    public Guid OrganisationId { get; private init; }

    public Guid FreizeitId { get; private init; }

    /// <summary>Das eingebundene Org-Dokument; <c>null</c> bei einer eigenen Datei.</summary>
    public Guid? OrgDokumentId { get; private init; }

    /// <summary>Titel einer eigenen Datei; bei einer Einbindung <c>null</c> (den Titel pflegt die Organisation).</summary>
    public string? Titel { get; private set; }

    public DokumentSichtbarkeit Sichtbarkeit { get; private set; }

    /// <summary>Am Inhalt erkannte Art einer eigenen Datei; bei einer Einbindung <c>null</c>.</summary>
    public DateiArt? Dateiart { get; private init; }

    /// <summary>Größe einer eigenen Datei in Bytes; bei einer Einbindung <c>null</c>.</summary>
    public long? Groesse { get; private init; }

    public DateTimeOffset HochgeladenAm { get; private init; }

    /// <summary>Version der Zeile (<c>xmin</c>, AD-19).</summary>
    public uint Version { get; private set; }

    public bool AusOrganisation => OrgDokumentId is not null;

    public static Dokument Hochladen(
        Guid id, Guid organisationId, Guid freizeitId, string titel, DokumentSichtbarkeit sichtbarkeit, DateiArt art, long groesse, DateTimeOffset jetzt) => new()
    {
        Id = id,
        OrganisationId = organisationId,
        FreizeitId = freizeitId,
        Titel = titel,
        Sichtbarkeit = sichtbarkeit,
        Dateiart = art,
        Groesse = groesse,
        HochgeladenAm = jetzt,
    };

    /// <summary>Bindet ein Org-Dokument ein, mit der sicheren Vorgabe <see cref="DokumentSichtbarkeit.Team"/>.</summary>
    public static Dokument Einbinden(Guid id, Guid organisationId, Guid freizeitId, Guid orgDokumentId, DateTimeOffset jetzt) => new()
    {
        Id = id,
        OrganisationId = organisationId,
        FreizeitId = freizeitId,
        OrgDokumentId = orgDokumentId,
        Sichtbarkeit = DokumentSichtbarkeit.Team,
        HochgeladenAm = jetzt,
    };

    /// <summary>Der Wert eines Felds als Text, wie ihn PATCH und Antwort tragen (die Sichtbarkeit als Name).</summary>
    public string? Wert(DokumentFeld feld) => feld switch
    {
        DokumentFeld.Titel => Titel,
        DokumentFeld.Sichtbarkeit => Sichtbarkeit.ToString(),
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };

    /// <summary>
    /// Ändert genau ein Feld (AD-19). Weicht der aktuelle Wert vom mitgeschickten alten Wert ab, ist das ein Konflikt. Den Titel einer
    /// Einbindung ändert nur die Organisation. Gültigkeit der Werte (Länge, bekannte Sichtbarkeit) prüft der Validator.
    /// </summary>
    public DokumentFeldErgebnis FeldAendern(DokumentFeld feld, string? alterWert, string? neuerWert)
    {
        if (feld == DokumentFeld.Titel && AusOrganisation)
        {
            return DokumentFeldErgebnis.NurOrganisation;
        }

        // Die Sichtbarkeit vergleicht den Namen wie beim neuen Wert ohne Rücksicht auf Groß- und Kleinschreibung.
        var unveraendert = feld == DokumentFeld.Sichtbarkeit
            ? DokumentRegeln.Sichtbarkeit(alterWert) == Sichtbarkeit
            : string.Equals(Wert(feld), alterWert, StringComparison.Ordinal);
        if (!unveraendert)
        {
            return DokumentFeldErgebnis.Konflikt;
        }

        switch (feld)
        {
            case DokumentFeld.Titel:
                // Wie beim Titel aus dem Dateinamen: ohne Steuerzeichen und Zeilenumbrüche.
                Titel = DokumentDateien.OhneSteuerzeichen(neuerWert);
                break;
            case DokumentFeld.Sichtbarkeit:
                Sichtbarkeit = DokumentRegeln.Sichtbarkeit(neuerWert) ?? throw new ArgumentException("Unbekannte Sichtbarkeit.", nameof(neuerWert));
                break;
        }

        return DokumentFeldErgebnis.Gespeichert;
    }

    /// <summary>Keinen Titel ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(Dokument)}({Id})";
}

/// <summary>Gemeinsame Regeln der Dokumente.</summary>
internal static class DokumentRegeln
{
    /// <summary>Die Sichtbarkeit zu ihrem Namen (ohne Rücksicht auf Groß- und Kleinschreibung); Zahlen und Unbekanntes sind <c>null</c>.</summary>
    public static DokumentSichtbarkeit? Sichtbarkeit(string? text) =>
        !string.IsNullOrWhiteSpace(text) && text.Trim().All(char.IsAsciiLetter)
        && Enum.TryParse<DokumentSichtbarkeit>(text.Trim(), ignoreCase: true, out var sichtbarkeit) && Enum.IsDefined(sichtbarkeit)
            ? sichtbarkeit
            : null;

    /// <summary>Text am Feld <c>sichtbarkeit</c>.</summary>
    public const string SichtbarkeitUnbekannt = "Unbekannte Sichtbarkeit.";
}
