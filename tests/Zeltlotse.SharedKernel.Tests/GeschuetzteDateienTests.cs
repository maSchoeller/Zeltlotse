using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.SharedKernel.Dateien;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.Testing;

namespace Zeltlotse.SharedKernel.Tests;

/// <summary>
/// <see cref="IGeschuetzteDateien"/> (AD-14, AD-15): Dateien mit Personenbezug liegen nur als Chiffre im Dateispeicher,
/// verschlüsselt mit dem Schlüssel der Person; gelesen wird nur über den Dienst.
/// </summary>
public sealed class GeschuetzteDateienTests
{
    private static readonly Guid PersonA = Guid.Parse("0199b000-0000-7000-8000-0000000000a1");
    private static readonly Guid PersonB = Guid.Parse("0199b000-0000-7000-8000-0000000000b1");
    private static readonly byte[] Pdf = [.. "%PDF-1.7\nFührungszeugnis von A\n"u8, 1, 2, 3];

    private readonly Hauptschluessel _hauptschluessel = Hauptschluessel.AusBase64(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    private readonly SpeicherSchluesselQuelle _quelle = new();
    private readonly DateiSpeicherImSpeicher _speicher = new();

    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    private GeschuetzteDateien Neu() => new(_speicher, new Verschluesselung(_hauptschluessel, _quelle));

    [Fact]
    public async Task Rundlauf_liefert_den_Inhalt_und_im_Speicher_liegt_nur_Chiffre()
    {
        _quelle.Anlegen(PersonA, _hauptschluessel);
        var kennung = Guid.CreateVersion7();

        await Neu().SpeichernAsync("personen", kennung, PersonA, Pdf, Abbruch);

        var roh = Assert.Single(_speicher.Objekte);
        Assert.Equal($"personen/{kennung}", roh.Key);
        Assert.Equal("application/octet-stream", roh.Value.ContentType);
        Assert.NotEqual(Pdf, roh.Value.Inhalt);
        Assert.Null(DateiPruefung.ArtErkennen(roh.Value.Inhalt));
        Assert.DoesNotContain("Führungszeugnis", System.Text.Encoding.UTF8.GetString(roh.Value.Inhalt), StringComparison.Ordinal);
        Assert.Equal(Pdf, await Neu().LesenAsync("personen", kennung, Abbruch));
    }

    [Fact]
    public async Task Eine_neue_Datei_ersetzt_die_alte_unter_derselben_Kennung()
    {
        _quelle.Anlegen(PersonA, _hauptschluessel);
        var kennung = Guid.CreateVersion7();
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9, 9];

        await Neu().SpeichernAsync("personen", kennung, PersonA, Pdf, Abbruch);
        await Neu().SpeichernAsync("personen", kennung, PersonA, png, Abbruch);

        Assert.Single(_speicher.Objekte);
        Assert.Equal(png, await Neu().LesenAsync("personen", kennung, Abbruch));
    }

    [Fact]
    public async Task Fehlende_Datei_ergibt_null_und_Loeschen_entfernt_sie()
    {
        _quelle.Anlegen(PersonA, _hauptschluessel);
        var kennung = Guid.CreateVersion7();

        Assert.Null(await Neu().LesenAsync("personen", kennung, Abbruch));

        await Neu().SpeichernAsync("personen", kennung, PersonA, Pdf, Abbruch);
        await Neu().LoeschenAsync("personen", kennung, Abbruch);

        Assert.Empty(_speicher.Objekte);
        Assert.Null(await Neu().LesenAsync("personen", kennung, Abbruch));
    }

    [Fact]
    public async Task Einer_anderen_Person_untergeschoben_ist_die_Datei_nicht_lesbar()
    {
        _quelle.Anlegen(PersonA, _hauptschluessel);
        _quelle.Anlegen(PersonB, _hauptschluessel);
        var kennung = Guid.CreateVersion7();
        await Neu().SpeichernAsync("personen", kennung, PersonA, Pdf, Abbruch);

        // Die PersonId im Kopf auf B umschreiben: Der Schlüssel von B passt nicht, und die PersonId ist authentifiziert.
        var roh = _speicher.Objekte.Single().Value.Inhalt;
        PersonB.TryWriteBytes(roh.AsSpan(2, 16));
        _speicher.RohSetzen("personen", kennung, roh);

        await Assert.ThrowsAnyAsync<CryptographicException>(() => Neu().LesenAsync("personen", kennung, Abbruch));
    }

    [Fact]
    public async Task Ohne_Schluessel_der_Person_ist_die_Datei_nicht_mehr_lesbar()
    {
        _quelle.Anlegen(PersonA, _hauptschluessel);
        var kennung = Guid.CreateVersion7();
        await Neu().SpeichernAsync("personen", kennung, PersonA, Pdf, Abbruch);

        _quelle.Vernichten(PersonA);

        await Assert.ThrowsAsync<PersonenSchluesselFehltAusnahme>(() => Neu().LesenAsync("personen", kennung, Abbruch));
    }

    [Fact]
    public void Der_Dienst_ist_im_SharedKernel_registriert()
    {
        var services = new ServiceCollection();
        services.AddSharedKernel();

        Assert.Contains(services, d => d.ServiceType == typeof(IGeschuetzteDateien) && d.Lifetime == ServiceLifetime.Scoped);
    }
}
