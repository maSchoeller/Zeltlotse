namespace Zeltlotse.Personen.Contracts.Anmeldung;

/// <summary>
/// Lesende Schnittstelle von Personen (AD-2, AD-11, FR-17): prüft ein Gast-Token (Zweck <c>gast-link</c>). Freizeiten vertraut
/// keiner <c>PersonId</c> eines anonymen Aufrufers; die Personen einer Gast-Anmeldung kommen nur von hier.
/// </summary>
public interface IGastZugaenge
{
    /// <summary>
    /// Die Gast-Familie zum Token oder <c>null</c>: unbekannt, falsch geformt, anderer Zweck, abgelaufen oder schon von einem
    /// Konto übernommen. Gültig ist der Gast-Link, solange eine Freigabe einer Person der Familie läuft; vor der ersten Freigabe
    /// 1 Tag ab dem Anlegen. Liest nur, schreibt nichts.
    /// </summary>
    Task<GastZugangInfo?> PruefenAsync(string? token, CancellationToken abbruch = default);
}

/// <summary>Eine gültige Gast-Familie: Elternteil ohne Konto mit normalisierter Mail-Adresse und die Kinder der Familie.</summary>
public sealed record GastZugangInfo(Guid ElternPersonId, string Mail, IReadOnlyList<Guid> KindPersonIds, DateTimeOffset GueltigBis)
{
    /// <summary>Keine Mail-Adresse ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(GastZugangInfo)}({ElternPersonId})";
}
