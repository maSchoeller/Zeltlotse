using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Zeltlotse.SharedKernel.Datenschutz;

namespace Zeltlotse.SharedKernel.Tests;

/// <summary>Matrix-Zeilen „Schlüssel vernichtet“, „Manipuliert / falsche Person“, „Hauptschlüssel fehlt/ungültig“ (AD-14).</summary>
public sealed class VerschluesselungTests
{
    private static readonly Guid PersonA = Guid.Parse("0199b000-0000-7000-8000-00000000000a");
    private static readonly Guid PersonB = Guid.Parse("0199b000-0000-7000-8000-00000000000b");

    private readonly Hauptschluessel _hauptschluessel = Hauptschluessel.AusBase64(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    private readonly SpeicherSchluesselQuelle _quelle = new();

    private Verschluesselung Neu() => new(_hauptschluessel, _quelle);

    [Fact]
    public async Task Text_Rundlauf_liefert_den_Text_und_der_Chiffretext_enthaelt_ihn_nicht()
    {
        _quelle.Anlegen(PersonA, _hauptschluessel);
        var verschluesselung = Neu();

        var geschuetzt = await verschluesselung.VerschluesselnAsync(PersonA, "Erdnussallergie", TestContext.Current.CancellationToken);

        Assert.Equal(PersonA, geschuetzt.PersonId);
        Assert.DoesNotContain("Erdnuss", Encoding.UTF8.GetString(geschuetzt.AlsBytes()), StringComparison.Ordinal);
        Assert.Equal("Erdnussallergie", await verschluesselung.EntschluesselnAsync(geschuetzt, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Byte_Rundlauf_liefert_die_Bytes()
    {
        _quelle.Anlegen(PersonA, _hauptschluessel);
        var verschluesselung = Neu();
        byte[] inhalt = [0x25, 0x50, 0x44, 0x46, 1, 2, 3];

        var geschuetzt = await verschluesselung.VerschluesselnAsync(PersonA, inhalt, TestContext.Current.CancellationToken);

        Assert.Equal(inhalt, await verschluesselung.EntschluesselnBytesAsync(geschuetzt, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Gleicher_Text_ergibt_jedes_Mal_einen_anderen_Chiffretext()
    {
        _quelle.Anlegen(PersonA, _hauptschluessel);
        var verschluesselung = Neu();

        var eins = await verschluesselung.VerschluesselnAsync(PersonA, "gleich", TestContext.Current.CancellationToken);
        var zwei = await verschluesselung.VerschluesselnAsync(PersonA, "gleich", TestContext.Current.CancellationToken);

        Assert.NotEqual(eins.AlsBytes(), zwei.AlsBytes());
    }

    [Fact]
    public async Task Fehlender_Schluessel_beim_Entschluesseln_wirft_PersonenSchluesselFehltAusnahme()
    {
        _quelle.Anlegen(PersonA, _hauptschluessel);
        var verschluesselung = Neu();
        var geschuetzt = await verschluesselung.VerschluesselnAsync(PersonA, "Notfallnummer", TestContext.Current.CancellationToken);

        _quelle.Vernichten(PersonA);

        var fehler = await Assert.ThrowsAsync<PersonenSchluesselFehltAusnahme>(
            () => verschluesselung.EntschluesselnAsync(geschuetzt, TestContext.Current.CancellationToken));
        Assert.Equal(PersonA, fehler.PersonId);
    }

    [Fact]
    public async Task Fehlender_Schluessel_beim_Verschluesseln_wirft_PersonenSchluesselFehltAusnahme()
    {
        await Assert.ThrowsAsync<PersonenSchluesselFehltAusnahme>(
            () => Neu().VerschluesselnAsync(PersonA, "x", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Veraenderter_Chiffretext_wirft_und_liefert_keinen_Klartext()
    {
        _quelle.Anlegen(PersonA, _hauptschluessel);
        var verschluesselung = Neu();
        var bytes = (await verschluesselung.VerschluesselnAsync(PersonA, "Asthma", TestContext.Current.CancellationToken)).AlsBytes();
        bytes[^1] ^= 0x01;

        await Assert.ThrowsAnyAsync<CryptographicException>(
            () => verschluesselung.EntschluesselnAsync(Geschuetzt.AusBytes(bytes), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Einer_anderen_Person_untergeschobener_Chiffretext_wirft()
    {
        _quelle.Anlegen(PersonA, _hauptschluessel);
        _quelle.Anlegen(PersonB, _hauptschluessel);
        var verschluesselung = Neu();
        var bytes = (await verschluesselung.VerschluesselnAsync(PersonA, "Diabetes", TestContext.Current.CancellationToken)).AlsBytes();

        // PersonId im Kopf auf Person B umschreiben: anderer Schlüssel und andere Authentifizierungsdaten.
        PersonB.ToByteArray().CopyTo(bytes, 2);
        var untergeschoben = Geschuetzt.AusBytes(bytes);
        Assert.Equal(PersonB, untergeschoben.PersonId);

        await Assert.ThrowsAnyAsync<CryptographicException>(
            () => verschluesselung.EntschluesselnAsync(untergeschoben, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Schluessel_einer_anderen_Person_unter_falscher_Kennung_wird_beim_Auspacken_abgelehnt()
    {
        // Umhüllter Schlüssel von B wird als Schlüssel von A ausgegeben: Die Umhüllung bindet die PersonId.
        _quelle.Setzen(PersonA, _hauptschluessel.NeuenPersonenSchluesselUmhuellen(PersonB));

        await Assert.ThrowsAnyAsync<CryptographicException>(
            () => Neu().VerschluesselnAsync(PersonA, "x", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Text_laesst_sich_nicht_als_Bytes_lesen_und_umgekehrt()
    {
        _quelle.Anlegen(PersonA, _hauptschluessel);
        var verschluesselung = Neu();
        var text = await verschluesselung.VerschluesselnAsync(PersonA, "Text", TestContext.Current.CancellationToken);
        var bytes = await verschluesselung.VerschluesselnAsync(PersonA, [1, 2, 3], TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => verschluesselung.EntschluesselnBytesAsync(text, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => verschluesselung.EntschluesselnAsync(bytes, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Unbekannte_Formatversion_wird_abgelehnt()
    {
        var bytes = new byte[60];
        bytes[0] = 99;

        Assert.ThrowsAny<CryptographicException>(() => Geschuetzt.AusBytes(bytes));
    }

    [Fact]
    public void Zu_kurzer_Chiffretext_wird_abgelehnt()
    {
        Assert.ThrowsAny<CryptographicException>(() => Geschuetzt.AusBytes([1, 1, 2]));
    }

    [Fact]
    public void Geschuetzt_vergleicht_nach_Inhalt()
    {
        var bytes = new byte[50];
        bytes[0] = 1;
        bytes[1] = 1;

        Assert.Equal(Geschuetzt.AusBytes(bytes), Geschuetzt.AusBytes([.. bytes]));
        Assert.Equal(Geschuetzt.AusBytes(bytes).GetHashCode(), Geschuetzt.AusBytes([.. bytes]).GetHashCode());
    }

    [Fact]
    public void Nicht_kanonisches_Base64_wie_aus_Aspire_wird_angenommen()
    {
        // 43 Zeichen aus dem Base64-Alphabet plus "=": gesetzte Füllbits im letzten Zeichen (so erzeugt der AppHost den Schlüssel).
        var schluessel = Hauptschluessel.AusBase64(string.Concat(Enumerable.Repeat("Zz9", 14)) + "B=");

        Assert.NotEmpty(schluessel.NeuenPersonenSchluesselUmhuellen(PersonA));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("kein-base64!")]
    [InlineData("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8g")] // 33 Bytes
    [InlineData("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHg==")] // 31 Bytes
    public void Fehlender_oder_ungueltiger_Hauptschluessel_bricht_ab_ohne_den_Wert_zu_nennen(string? wert)
    {
        var konfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [Hauptschluessel.Konfigurationsschluessel] = wert })
            .Build();

        var fehler = Assert.Throws<InvalidOperationException>(() => Hauptschluessel.AusKonfiguration(konfiguration));

        Assert.Contains(Hauptschluessel.Konfigurationsschluessel, fehler.Message, StringComparison.Ordinal);
        if (!string.IsNullOrEmpty(wert))
        {
            Assert.DoesNotContain(wert, fehler.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Gueltiger_Hauptschluessel_nennt_sich_in_ToString_nicht()
    {
        var wert = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var schluessel = Hauptschluessel.AusBase64(wert);

        Assert.DoesNotContain(wert, schluessel.ToString(), StringComparison.Ordinal);
    }
}

/// <summary>Personen-Schlüssel im Speicher, für Tests ohne das Modul Personen.</summary>
internal sealed class SpeicherSchluesselQuelle : IPersonenSchluesselQuelle
{
    private readonly Dictionary<Guid, byte[]> _schluessel = [];

    public void Anlegen(Guid personId, Hauptschluessel hauptschluessel) =>
        Setzen(personId, hauptschluessel.NeuenPersonenSchluesselUmhuellen(personId));

    public void Setzen(Guid personId, byte[] umhuellt)
    {
        lock (_schluessel)
        {
            _schluessel[personId] = umhuellt;
        }
    }

    public void Vernichten(Guid personId)
    {
        lock (_schluessel)
        {
            _schluessel.Remove(personId);
        }
    }

    public Task<byte[]?> UmhuelltenSchluesselLesenAsync(Guid personId, CancellationToken abbruch = default)
    {
        lock (_schluessel)
        {
            return Task.FromResult(_schluessel.TryGetValue(personId, out var umhuellt) ? umhuellt : null);
        }
    }
}
