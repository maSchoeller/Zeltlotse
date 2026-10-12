using System.Security.Cryptography;
using System.Text;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Zugangslinks;

namespace Zeltlotse.SharedKernel.Tests;

/// <summary>
/// Dauerhaft geteilte Links (AD-11): verschlüsselt gespeichert, damit sie sich wieder anzeigen lassen. AES-256-GCM mit einem per
/// HKDF aus dem Hauptschlüssel abgeleiteten Schlüssel; Bezug und Zweck stehen in den Zusatzdaten.
/// </summary>
public sealed class LinkSchutzTests
{
    private const string Zweck = "anmeldelink-teilnehmende";

    private static readonly Guid FreizeitA = Guid.Parse("0199b000-0000-7000-8000-0000000000aa");
    private static readonly Guid FreizeitB = Guid.Parse("0199b000-0000-7000-8000-0000000000bb");

    private readonly Hauptschluessel _hauptschluessel = Hauptschluessel.AusBase64(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

    private LinkSchutz Neu() => new(_hauptschluessel);

    [Fact]
    public void Rundlauf_liefert_das_Token_und_der_Chiffretext_enthaelt_es_nicht()
    {
        var token = Zugangslink.NeuesToken();
        var schutz = Neu();

        var geschuetzt = schutz.Schuetzen(token, FreizeitA, Zweck);

        Assert.DoesNotContain(token, Encoding.ASCII.GetString(geschuetzt), StringComparison.Ordinal);
        Assert.Equal(token, schutz.Lesen(geschuetzt, FreizeitA, Zweck));
    }

    [Fact]
    public void Gleiches_Token_ergibt_jedes_Mal_einen_anderen_Chiffretext()
    {
        var token = Zugangslink.NeuesToken();
        var schutz = Neu();

        Assert.NotEqual(schutz.Schuetzen(token, FreizeitA, Zweck), schutz.Schuetzen(token, FreizeitA, Zweck));
    }

    [Fact]
    public void Anderer_Bezug_oder_anderer_Zweck_laesst_sich_nicht_lesen()
    {
        var schutz = Neu();
        var geschuetzt = schutz.Schuetzen(Zugangslink.NeuesToken(), FreizeitA, Zweck);

        Assert.ThrowsAny<CryptographicException>(() => schutz.Lesen(geschuetzt, FreizeitB, Zweck));
        Assert.ThrowsAny<CryptographicException>(() => schutz.Lesen(geschuetzt, FreizeitA, "anmeldelink-mitarbeitende"));
    }

    [Fact]
    public void Veraenderter_Chiffretext_oder_anderer_Hauptschluessel_wirft()
    {
        var geschuetzt = Neu().Schuetzen(Zugangslink.NeuesToken(), FreizeitA, Zweck);
        var veraendert = geschuetzt.ToArray();
        veraendert[^1] ^= 0x01;
        var fremd = new LinkSchutz(Hauptschluessel.AusBase64(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));

        Assert.ThrowsAny<CryptographicException>(() => Neu().Lesen(veraendert, FreizeitA, Zweck));
        Assert.ThrowsAny<CryptographicException>(() => fremd.Lesen(geschuetzt, FreizeitA, Zweck));
        Assert.ThrowsAny<CryptographicException>(() => Neu().Lesen([1, 2, 3], FreizeitA, Zweck));
    }

    [Fact]
    public void Abgeleiteter_Schluessel_ist_nicht_der_Hauptschluessel_und_haengt_am_Zweck()
    {
        Assert.NotEqual(_hauptschluessel.Ableiten("zugangslink"), _hauptschluessel.Ableiten("anderes"));
        Assert.Equal(_hauptschluessel.Ableiten("zugangslink"), _hauptschluessel.Ableiten("zugangslink"));
        Assert.Equal(32, _hauptschluessel.Ableiten("zugangslink").Length);
    }
}
