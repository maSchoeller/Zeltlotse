namespace Zeltlotse.Api.Contracts.Personen;

/// <summary>
/// Antwort von <c>GET /api/ich/familie</c> (AD-26): nur die eigene Familie. Ohne Familie sind alle Listen leer; wer Eltern
/// hat, ist selbst Elternteil.
/// </summary>
/// <param name="Eltern">Die Eltern der Familie (höchstens 2), nur mit Namen.</param>
/// <param name="Kinder">Die Kinderprofile der Familie, nach Geburtsdatum.</param>
/// <param name="Einladungen">Nur offene, nicht abgelaufene Einladungen eines zweiten Elternteils.</param>
public sealed record FamilieAntwort(
    IReadOnlyList<ElternteilZeile> Eltern,
    IReadOnlyList<KindZeile> Kinder,
    IReadOnlyList<OffeneFamilienEinladung> Einladungen);

/// <summary>Ein Elternteil der Familie; Namen ohne Profil sind <c>null</c>.</summary>
public sealed record ElternteilZeile(Guid PersonId, string? Vorname, string? Nachname);

/// <summary>Ein Kinderprofil der Familie mit seiner Version (AD-19).</summary>
/// <param name="Volljaehrig">Vom Server berechnet (18. Geburtstag, Kalendertag in Europe/Berlin); der Client rechnet nie selbst.</param>
/// <param name="Uebergabe">Die offene, nicht abgelaufene Profil-Übergabe, falls es eine gibt.</param>
public sealed record KindZeile(
    Guid PersonId,
    uint Version,
    string? Vorname,
    string? Nachname,
    DateOnly Geburtsdatum,
    bool Volljaehrig,
    KindUebergabe? Uebergabe);

/// <summary>Eine offene Profil-Übergabe: an welche Mail-Adresse und bis wann.</summary>
public sealed record KindUebergabe(string Mail, DateTimeOffset GueltigBis);

/// <summary>Eine offene Einladung eines zweiten Elternteils.</summary>
public sealed record OffeneFamilienEinladung(Guid Id, string Mail, DateTimeOffset GueltigBis);

/// <summary>Körper von <c>POST /api/ich/familie/kinder</c>: Vorname und Geburtsdatum sind Pflicht, der Nachname nicht.</summary>
public sealed record KindHinzufuegen(string? Vorname, string? Nachname, DateOnly? Geburtsdatum);

/// <summary>Antwort <c>201</c> von <c>POST /api/ich/familie/kinder</c>.</summary>
public sealed record KindHinzugefuegt(Guid PersonId);

/// <summary>Körper von <c>POST /api/ich/familie/einladungen</c>: die Mail-Adresse des zweiten Elternteils.</summary>
public sealed record ElternteilEinladen(string? Mail);

/// <summary>Antwort <c>202</c> von <c>POST /api/ich/familie/einladungen</c>.</summary>
public sealed record ElternteilEingeladen(Guid Id);

/// <summary>Körper von <c>POST /api/ich/familie/kinder/{personId}/uebergabe</c>: die Mail-Adresse des volljährigen Kindes.</summary>
public sealed record ProfilUebergeben(string? Mail);

/// <summary>Antwort <c>202</c> von <c>POST /api/ich/familie/kinder/{personId}/uebergabe</c>.</summary>
public sealed record ProfilUebergabeVerschickt(Guid Id);

/// <summary>Körper von <c>POST /api/personen/familie/einladungen/annehmen</c>; das Token nur im Körper.</summary>
public sealed record FamilienEinladungAnnehmen(string? Token);

/// <summary>Antwort <c>200</c> von <c>POST /api/personen/familie/einladungen/annehmen</c>: <c>{}</c>.</summary>
public sealed record FamilienEinladungAngenommen;

/// <summary>Körper von <c>POST /api/personen/uebergabe/annehmen</c>; das Token nur im Körper.</summary>
public sealed record ProfilUebernehmen(string? Token);

/// <summary>Antwort <c>200</c> von <c>POST /api/personen/uebergabe/annehmen</c>: das übernommene Profil.</summary>
public sealed record ProfilUebernommen(Guid PersonId);

/// <summary>
/// Bearbeitbare Felder eines Kinderprofils für <c>PATCH /api/ich/familie/kinder/{personId}</c> (AD-19); JSON-Namen in
/// <see cref="KindFelder"/>.
/// </summary>
public enum KindFeld
{
    Vorname,
    Nachname,
    Geburtsdatum,
}

/// <summary>
/// JSON-Namen der Felder für <c>PATCH /api/ich/familie/kinder/{personId}</c>: Körper
/// <c>{ "version": n, "alterWert": …, "&lt;feld&gt;": neu }</c>, Antwort <c>{ "version": n, "&lt;feld&gt;": gespeichert }</c>.
/// Das Geburtsdatum steht als Text im <see cref="Datumsformat"/>; Namen haben höchstens <see cref="MaximaleLaenge"/> Zeichen.
/// </summary>
public static class KindFelder
{
    public const string Vorname = "vorname";

    public const string Nachname = "nachname";

    public const string Geburtsdatum = "geburtsdatum";

    public const int MaximaleLaenge = 100;

    /// <summary>Format des Geburtsdatums im Körper und in der Antwort.</summary>
    public const string Datumsformat = "yyyy-MM-dd";

    public static string JsonName(KindFeld feld) => feld switch
    {
        KindFeld.Vorname => Vorname,
        KindFeld.Nachname => Nachname,
        KindFeld.Geburtsdatum => Geburtsdatum,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };
}
