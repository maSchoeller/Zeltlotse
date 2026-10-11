using Microsoft.AspNetCore.Builder;

namespace Zeltlotse.SharedKernel.Anmeldung;

/// <summary>
/// Erneute Anmeldung (AD-25): Endpunkte mit diesem Merkmal verlangen eine Anmeldung, die höchstens
/// <see cref="Sitzung.FrischeDauer"/> zurückliegt. Die zentrale Rechteprüfung des Hosts antwortet sonst mit <c>401</c>; der
/// Client führt dann wie bei jedem <c>401</c> zur Anmeldung mit Rückweg.
/// </summary>
public static class ErneuteAnmeldungErweiterung
{
    public static TBuilder ErneuteAnmeldung<TBuilder>(this TBuilder endpunkt)
        where TBuilder : IEndpointConventionBuilder =>
        endpunkt.WithMetadata(ErneuteAnmeldungMetadaten.Instanz);
}

/// <summary>Merkmal an einem Endpunkt, der eine frische Anmeldung verlangt (AD-25).</summary>
public sealed class ErneuteAnmeldungMetadaten
{
    public static ErneuteAnmeldungMetadaten Instanz { get; } = new();

    private ErneuteAnmeldungMetadaten()
    {
    }
}
