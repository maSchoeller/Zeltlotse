using Zeltlotse.SharedKernel.Datenschutz;

namespace Zeltlotse.SharedKernel.Tests;

/// <summary>Matrix-Zeile „Browser-Fehler“: Seite ohne Abfrage, Fragment und Token-Segmente (AD-22).</summary>
public sealed class PfadBereinigungTests
{
    [Theory]
    [InlineData("/app/gast/AbCdEfGhIjKlMnOpQrStUvWxYz012345/pass?code=1#x", "/app/gast/***/pass")]
    [InlineData("/app/freizeiten/0199b000-0000-7000-8000-00000000000a/kueche", "/app/freizeiten/0199b000-0000-7000-8000-00000000000a/kueche")]
    [InlineData("/app/freizeiten/0199b0000000700080000000000000aa", "/app/freizeiten/***")]
    [InlineData("/anmeldung/abcdefghij_klmnopqrst-u", "/anmeldung/***")]
    [InlineData("/anmeldung/abcdefghij_klmnopqrst", "/anmeldung/abcdefghij_klmnopqrst")]
    [InlineData("/app#token=AbCdEfGhIjKlMnOpQrStUvWxYz", "/app")]
    [InlineData("/app/kurz?x=AbCdEfGhIjKlMnOpQrStUvWxYz", "/app/kurz")]
    [InlineData("/", "/")]
    [InlineData("", "")]
    [InlineData("https://zeltlotse.example/app/gast/AbCdEfGhIjKlMnOpQrStUvWxYz012345", "/app/gast/***")]
    public void Seite_wird_bereinigt(string seite, string erwartet)
    {
        Assert.Equal(erwartet, PfadBereinigung.Bereinigen(seite));
    }
}
