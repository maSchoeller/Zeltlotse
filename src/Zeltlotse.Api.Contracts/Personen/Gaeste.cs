namespace Zeltlotse.Api.Contracts.Personen;

/// <summary>
/// Körper von <c>POST /api/personen/oeffentlich/gaeste</c> (anonym, Gast-Anmeldung, FR-17): Elternteil ohne Konto mit Mail und
/// ein Kind unter 18. Pflicht sind Vorname und Mail des Elternteils, Vorname und Geburtsdatum des Kindes (NFR-12).
/// </summary>
public sealed record GastAnlegen(
    string? ElternVorname,
    string? ElternNachname,
    string? Mail,
    string? KindVorname,
    string? KindNachname,
    DateOnly? KindGeburtsdatum)
{
    /// <summary>Keine Namen oder Mail-Adressen ausgeben (AD-22).</summary>
    public override string ToString() => nameof(GastAnlegen);
}

/// <summary>
/// Antwort <c>201</c> von <c>POST /api/personen/oeffentlich/gaeste</c>: das Gast-Token (Gast-Link) und das angelegte Kind. Das
/// Token steht nur hier und in der Eingangsmail.
/// </summary>
public sealed record GastAngelegt(string GastToken, Guid KindPersonId)
{
    /// <summary>Nie das Token ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(GastAngelegt)}({KindPersonId})";
}

/// <summary>Körper von <c>POST /api/personen/oeffentlich/gaeste/{gastToken}/kinder</c>: ein weiteres Kind unter 18 der Gast-Familie.</summary>
public sealed record GastKindHinzufuegen(string? Vorname, string? Nachname, DateOnly? Geburtsdatum)
{
    /// <summary>Keine Namen ausgeben (AD-22).</summary>
    public override string ToString() => nameof(GastKindHinzufuegen);
}

/// <summary>Antwort <c>201</c> von <c>POST /api/personen/oeffentlich/gaeste/{gastToken}/kinder</c>.</summary>
public sealed record GastKindAngelegt(Guid KindPersonId);

/// <summary>Körper von <c>POST /api/ich/gastprofil/uebernehmen</c>: das Gast-Token aus dem Gast-Link.</summary>
public sealed record GastprofilUebernehmen(string? GastToken)
{
    /// <summary>Nie das Token ausgeben (AD-22).</summary>
    public override string ToString() => nameof(GastprofilUebernehmen);
}

/// <summary>Antwort von <c>POST /api/ich/gastprofil/uebernehmen</c>: das Profil des Elternteils, das ab der neuen Anmeldung zum Konto gehört.</summary>
public sealed record GastprofilUebernommen(Guid PersonId);

/// <summary>JSON-Namen der Felder der Gast-Endpunkte (Fehlerschlüssel in ProblemDetails, AD-27).</summary>
public static class GastFelder
{
    public const string ElternVorname = "elternVorname";

    public const string ElternNachname = "elternNachname";

    public const string Mail = "mail";

    public const string KindVorname = "kindVorname";

    public const string KindNachname = "kindNachname";

    public const string KindGeburtsdatum = "kindGeburtsdatum";

    public const string Vorname = "vorname";

    public const string Nachname = "nachname";

    public const string Geburtsdatum = "geburtsdatum";

    /// <summary>Fehlertext, wenn das Kind schon 18 ist: Volljährige besitzen ihre Daten selbst (FR-18).</summary>
    public const string Volljaehrig = "Volljährige melden sich mit Konto an.";
}
