namespace Zeltlotse.Freizeiten.Contracts.Zeitpunkte;

/// <summary>
/// Die einzige Quelle für Phase, Freizeitende und „gültig bis“ des Freigabefensters (AD-17). Alle Module rechnen damit,
/// keines selbst. Läuft unter dem Mandantenfilter: Freizeiten anderer Organisationen fehlen im Ergebnis.
/// </summary>
public interface IFreizeitZeitpunkte
{
    /// <summary>Je vorhandener Freizeit ihre Zeitpunkte; unbekannte Kennungen fehlen im Ergebnis (Menge statt Einzelabruf).</summary>
    Task<IReadOnlyDictionary<Guid, FreizeitZeitpunkte>> ZeitpunkteAsync(IReadOnlyCollection<Guid> freizeitIds, CancellationToken abbruch = default);
}

/// <param name="Phase">Phase zum jetzigen Zeitpunkt der Uhr.</param>
/// <param name="Freizeitende">Ende des letzten Freizeittages: 24:00 Uhr Europe/Berlin.</param>
/// <param name="FreigabeGueltigBis">Ende des Freigabefensters: Freizeitende plus das festgeschriebene Intervall in Kalendertagen.</param>
public sealed record FreizeitZeitpunkte(FreizeitPhase Phase, DateTimeOffset Freizeitende, DateTimeOffset FreigabeGueltigBis);

/// <summary>Phase einer Freizeit (AD-17): nur der Abschluss ist gespeichert, alles andere folgt aus Datum und Uhr.</summary>
public enum FreizeitPhase
{
    InVorbereitung = 1,
    Laeuft = 2,
    Nachbereitung = 3,
    Gelaufen = 4,
}
