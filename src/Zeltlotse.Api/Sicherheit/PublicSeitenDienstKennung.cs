using System.Security.Cryptography;
using System.Text;

namespace Zeltlotse.Api.Sicherheit;

/// <summary>
/// Dienst-Kennung der öffentlichen Seiten (AD-5, AD-25): ein zufälliges 256-Bit-Geheimnis je Prozessstart, das
/// <c>Zeltlotse.Public</c> über DI erhält und als <c>X-Zeltlotse-Dienst</c> mitschickt. Die Ausnahme vom Antiforgery-Zwang
/// gilt damit nur für Aufrufe aus dem eigenen Prozess und überlebt einen späteren eigenen Container nicht ohne bewusste Änderung.
/// </summary>
public sealed class PublicSeitenDienstKennung
{
    private readonly byte[] _geheimnis;

    public PublicSeitenDienstKennung()
    {
        Geheimnis = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        _geheimnis = Encoding.ASCII.GetBytes(Geheimnis);
    }

    /// <summary>Nur für die öffentlichen Seiten im selben Prozess; nie loggen.</summary>
    public string Geheimnis { get; }

    /// <summary>Vergleich in fester Zeit.</summary>
    public bool Pruefen(string? wert) =>
        !string.IsNullOrEmpty(wert) && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(wert), _geheimnis);

    public override string ToString() => nameof(PublicSeitenDienstKennung);
}
