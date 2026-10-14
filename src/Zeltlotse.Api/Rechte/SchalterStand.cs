using Zeltlotse.Freizeiten.Contracts.Schalter;
using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Api.Rechte;

/// <summary>
/// <see cref="ISchalterStand"/> im API-Host (AD-17, AD-20): die Schalter einer Freizeit aus Freizeiten, je Freizeit höchstens einmal pro
/// Anfrage gelesen (Scope = Anfrage) und für Rechteprüfung und Pipeline geteilt. Der Mandanten-Kontext ist dann schon gesetzt.
/// </summary>
internal sealed class SchalterStand(IFreizeitSchalterLesen schalter) : ISchalterStand
{
    private readonly Dictionary<Guid, IReadOnlyCollection<FunktionsSchalter>> _an = [];

    public async Task<bool> IstAnAsync(Guid freizeitId, FunktionsSchalter funktionsSchalter, CancellationToken abbruch = default)
    {
        if (!_an.TryGetValue(freizeitId, out var an))
        {
            an = await schalter.SchalterAsync(freizeitId, abbruch);
            _an[freizeitId] = an;
        }

        return an.Contains(funktionsSchalter);
    }
}
