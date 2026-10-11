using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Ui.Api;

namespace Zeltlotse.Ui.Kontext;

/// <summary>
/// Freizeit-Kontext (AD-29): Rollen, Phase, Schalter und erlaubte Aktionen aus genau einem Endpunkt je Freizeit.
/// Kein Cache und kein globaler Zustand: Seiten laden beim Öffnen. Rechte entscheidet nur <see cref="Darf"/>.
/// </summary>
public sealed class FreizeitKontextDienst(FreizeitenApiClient api)
{
    /// <summary>Lädt den Kontext; <c>null</c> heißt „nicht verfügbar“ (<c>403</c> oder <c>404</c>).</summary>
    public Task<FreizeitKontext?> LadenAsync(Guid freizeitId, CancellationToken abbruch = default) =>
        api.KontextLadenAsync(freizeitId, abbruch);

    /// <summary>Ob die Aktion erlaubt ist: nur nach <see cref="FreizeitKontext.Erlaubt"/>, nie aus den Rollen abgeleitet.</summary>
    public static bool Darf(FreizeitKontext kontext, string schluessel)
    {
        ArgumentNullException.ThrowIfNull(kontext);
        return kontext.Erlaubt.Contains(schluessel, StringComparer.Ordinal);
    }
}
