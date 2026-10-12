namespace Zeltlotse.Freizeiten.Contracts.Zeitpunkte;

/// <summary>
/// Lesende Schnittstelle von Freizeiten für den Lösch-Dienst des API-Hosts (AD-16, AD-17): alle Freizeiten mit Organisation und
/// Freizeitende. Liest ohne Mandantenfilter; nur der Lösch-Dienst ruft sie auf (Ausnahme laut AD-6).
/// </summary>
public interface IFreizeitEndenLesen
{
    Task<IReadOnlyList<FreizeitEnde>> AlleAsync(CancellationToken abbruch = default);
}

/// <param name="Freizeitende">Ende des letzten Freizeittages: 24:00 Uhr Europe/Berlin (AD-17).</param>
public sealed record FreizeitEnde(Guid FreizeitId, Guid OrganisationId, DateTimeOffset Freizeitende);
