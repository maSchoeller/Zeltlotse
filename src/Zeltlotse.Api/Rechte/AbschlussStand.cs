using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Api.Rechte;

/// <summary>
/// Story 3-8: <see cref="IAbschlussStand"/> im API-Host (AD-17): die Phase einer Freizeit aus <see cref="IFreizeitZeitpunkte"/>, je Freizeit
/// höchstens einmal pro Anfrage gelesen (Scope = Anfrage) und für die Schreibschutz-Stufe der Pipeline geteilt. Der Mandanten-Kontext ist
/// dann schon gesetzt; eine Freizeit außerhalb des Mandanten fehlt im Ergebnis und gilt als nicht gelaufen.
/// </summary>
internal sealed class AbschlussStand(IFreizeitZeitpunkte zeitpunkte) : IAbschlussStand
{
    private readonly Dictionary<Guid, bool> _abgeschlossen = [];

    public async Task<bool> IstAbgeschlossenAsync(Guid freizeitId, CancellationToken abbruch = default)
    {
        if (!_abgeschlossen.TryGetValue(freizeitId, out var abgeschlossen))
        {
            var gefunden = await zeitpunkte.ZeitpunkteAsync([freizeitId], abbruch);
            abgeschlossen = gefunden.TryGetValue(freizeitId, out var z) && z.Phase == FreizeitPhase.Gelaufen;
            _abgeschlossen[freizeitId] = abgeschlossen;
        }

        return abgeschlossen;
    }
}
