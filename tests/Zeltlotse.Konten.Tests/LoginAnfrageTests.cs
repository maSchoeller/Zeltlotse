using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using Zeltlotse.Konten.Domaene;
using Zeltlotse.SharedKernel.Zugangslinks;

namespace Zeltlotse.Konten.Tests;

/// <summary>Fachlogik der Login-Anfrage ohne Datenbank (AD-11, AD-28): Ablauf, einmal, Verwerfen, fünf Fehlversuche.</summary>
public sealed class LoginAnfrageTests
{
    private readonly FakeTimeProvider _uhr = new(new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero));

    private LoginAnfrage Neu() =>
        LoginAnfrage.Anlegen(Guid.CreateVersion7(), "a@example.org", [1], [2], _uhr.GetUtcNow());

    [Fact]
    public void Einloesbar_bis_kurz_vor_15_Minuten_danach_abgelaufen()
    {
        var anfrage = Neu();

        _uhr.Advance(TimeSpan.FromMinutes(15) - TimeSpan.FromTicks(1));
        Assert.True(anfrage.IstEinloesbar(_uhr.GetUtcNow()));

        _uhr.Advance(TimeSpan.FromTicks(1));
        Assert.False(anfrage.IstEinloesbar(_uhr.GetUtcNow()));
        Assert.Throws<InvalidOperationException>(() => anfrage.Einloesen(_uhr.GetUtcNow()));
        Assert.Equal(LoginAnfrageStatus.Offen, anfrage.Status);
    }

    [Fact]
    public void Nur_einmal_einloesbar()
    {
        var anfrage = Neu();

        anfrage.Einloesen(_uhr.GetUtcNow());

        Assert.Equal(LoginAnfrageStatus.Eingeloest, anfrage.Status);
        Assert.False(anfrage.IstEinloesbar(_uhr.GetUtcNow()));
        Assert.Throws<InvalidOperationException>(() => anfrage.Einloesen(_uhr.GetUtcNow()));
    }

    [Fact]
    public void Verworfen_ist_nicht_mehr_einloesbar_und_eine_eingeloeste_bleibt_eingeloest()
    {
        var offen = Neu();
        var eingeloest = Neu();
        eingeloest.Einloesen(_uhr.GetUtcNow());

        offen.Verwerfen();
        eingeloest.Verwerfen();

        Assert.Equal(LoginAnfrageStatus.Verworfen, offen.Status);
        Assert.False(offen.IstEinloesbar(_uhr.GetUtcNow()));
        Assert.Equal(LoginAnfrageStatus.Eingeloest, eingeloest.Status);
    }

    [Fact]
    public void Nach_fuenf_Fehlversuchen_verworfen_nach_vier_noch_offen()
    {
        var anfrage = Neu();

        for (var i = 0; i < 4; i++)
        {
            anfrage.FehlversuchZaehlen();
        }

        Assert.True(anfrage.IstEinloesbar(_uhr.GetUtcNow()));
        anfrage.FehlversuchZaehlen();
        Assert.Equal(LoginAnfrageStatus.Verworfen, anfrage.Status);
        Assert.Equal(5, anfrage.Fehlversuche);
        anfrage.FehlversuchZaehlen();
        Assert.Equal(5, anfrage.Fehlversuche);
    }

    [Fact]
    public void Token_hat_43_Zeichen_base64url_und_ist_jedes_Mal_neu()
    {
        var tokens = Enumerable.Range(0, 50).Select(_ => Zugangslink.NeuesToken()).ToList();

        Assert.All(tokens, t => Assert.Matches("^[A-Za-z0-9_-]{43}$", t));
        Assert.All(tokens, t => Assert.True(Zugangslink.KannTokenSein(t)));
        Assert.Equal(tokens.Count, tokens.Distinct(StringComparer.Ordinal).Count());
        Assert.False(Zugangslink.KannTokenSein(tokens[0] + "A"));
        Assert.False(Zugangslink.KannTokenSein(tokens[0][..42] + "="));
        Assert.False(Zugangslink.KannTokenSein(null));
    }

    [Fact]
    public void Der_Zweck_steckt_im_Token_Hash()
    {
        var token = Zugangslink.NeuesToken();

        var login = Zugangslink.TokenHash("login", token);

        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes("login:" + token)), login);
        Assert.NotEqual(login, Zugangslink.TokenHash("einladung", token));
        Assert.Equal(32, login.Length);
    }

    [Fact]
    public void Code_hat_sechs_Ziffern_und_passt_nur_zu_seiner_Anfrage()
    {
        var codes = Enumerable.Range(0, 50).Select(_ => Geheimnisse.NeuerCode()).ToList();
        Assert.All(codes, c => Assert.Matches("^[0-9]{6}$", c));

        var id = Guid.CreateVersion7();
        var code = codes[0];
        var anfrage = LoginAnfrage.Anlegen(id, "a@example.org", [1], Geheimnisse.CodeHash(id, code), _uhr.GetUtcNow());
        var andere = LoginAnfrage.Anlegen(Guid.CreateVersion7(), "a@example.org", [1], Geheimnisse.CodeHash(id, code), _uhr.GetUtcNow());

        Assert.True(Geheimnisse.CodePasst(anfrage, code));
        Assert.False(Geheimnisse.CodePasst(andere, code));
        Assert.False(Geheimnisse.CodePasst(anfrage, code + "0"));
        Assert.False(Geheimnisse.CodePasst(anfrage, null));
        Assert.False(Geheimnisse.CodePasst(anfrage, "١٢٣٤٥٦"));
    }

    [Theory]
    [InlineData("  Jonas.Huettner@Example.ORG ", "jonas.huettner@example.org")]
    [InlineData("a@b.de", "a@b.de")]
    public void Mail_wird_getrimmt_und_klein_geschrieben(string eingabe, string erwartet) =>
        Assert.Equal(erwartet, MailAdresse.Normalisieren(eingabe));
}
