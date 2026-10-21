using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Api.Rechte;

/// <summary>
/// Story 6-4: <see cref="IQrGast"/> im API-Host, je Anfrage (scoped). Nur die <see cref="RechtePruefung"/> setzt ihn, nachdem sie die Gast-Sitzung eines
/// Endpunkts mit <c>.NurQrGast()</c> geprüft hat; sonst bleiben beide Werte <c>null</c>.
/// </summary>
internal sealed class QrGastKontext : IQrGast
{
    public Guid? FreizeitId { get; private set; }

    public Guid? GeraeteId { get; private set; }

    public Guid? Stempel { get; private set; }

    public void Setzen(Guid freizeitId, Guid geraeteId, Guid stempel)
    {
        if (FreizeitId is not null)
        {
            throw new InvalidOperationException("Der QR-Gast ist für diese Anfrage schon gesetzt.");
        }

        (FreizeitId, GeraeteId, Stempel) = (freizeitId, geraeteId, stempel);
    }
}
