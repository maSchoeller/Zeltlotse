using System.Net;
using System.Text.RegularExpressions;
using Zeltlotse.SharedKernel.Zugangslinks;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.DokumentHelfer;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 2-7, öffentliche Seiten (FR-43, FR-68, AD-5, AD-29): die Card „Dokumente“ auf der Freizeitseite nur mit öffentlichen Dokumenten,
/// der Link „Elterninfo“ im Gast-Link und die Seite „Elterninfo“ über den Gast-Link mit Links auf die Dateien; ungültig 404. Alles
/// statisch: kein Skript, kein <c>style=</c>, <c>noindex</c>.
/// </summary>
public sealed class DokumenteSeitenTests(PostgresContainer postgres, S3Container s3) : IAsyncLifetime
{
    private FreizeitpassSzenario _s = null!;

    public async ValueTask InitializeAsync() => _s = await FreizeitpassSzenario.AufbauenAsync(postgres, s3);

    public async ValueTask DisposeAsync() => await _s.DisposeAsync();

    [Fact]
    public async Task Die_Freizeitseite_zeigt_die_Card_Dokumente_nur_mit_oeffentlichen_Dokumenten()
    {
        var ohne = await HtmlAsync($"/freizeit/{_s.TeilnehmendeX}");
        Assert.DoesNotContain(">Dokumente</h2>", ohne, StringComparison.Ordinal);

        var x = FreizeitPfad(_s.X);
        await HochgeladenAsync(_s.Miriam, x, Pdf, "Infobrief.pdf", "ElternUndTeilnehmende");
        var anfahrt = await HochgeladenAsync(_s.Miriam, x, Pdf, "Anfahrt zum Zeltplatz.pdf", "Oeffentlich");

        var html = await HtmlAsync($"/freizeit/{_s.TeilnehmendeX}");

        Assert.Contains(">Dokumente</h2>", html, StringComparison.Ordinal);
        Assert.Contains($"<a href=\"/api/freizeiten/oeffentlich/{_s.TeilnehmendeX}/dokumente/{anfahrt}\">Anfahrt zum Zeltplatz (PDF)</a>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Infobrief", html, StringComparison.Ordinal);
        Statisch(html);
    }

    [Fact]
    public async Task Gast_Link_fuehrt_zur_Elterninfo_mit_Links_auf_die_Dateien_fremd_oder_ungueltig_404()
    {
        var x = FreizeitPfad(_s.X);
        var infobrief = await HochgeladenAsync(_s.Miriam, x, Pdf, "Infobrief.pdf", "ElternUndTeilnehmende");
        await HochgeladenAsync(_s.Miriam, x, Pdf, "Programmideen.pdf", "Team");
        var (token, _, anmeldung) = await _s.GastAnmeldenAsync("lina.eltern@example.org", "Lina");
        var pfad = $"/gast/{token}/elterninfo/{anmeldung}";

        var gastLink = await HtmlAsync($"/gast/{token}");
        Assert.Contains($"<a href=\"{pfad}\">Elterninfo</a>", gastLink, StringComparison.Ordinal);

        using var seite = await SeiteAsync(pfad);

        Assert.Equal(HttpStatusCode.OK, seite.StatusCode);
        var html = WebUtility.HtmlDecode(await seite.Content.ReadAsStringAsync(Abbruch));
        Assert.Matches("<h1[^>]*>Elterninfo</h1>", html);
        Assert.Contains($">{FreizeitpassSzenario.Kapf}</p>", html, StringComparison.Ordinal);
        Assert.Contains(
            $"<a href=\"/api/freizeiten/gast/{token}/anmeldungen/{anmeldung}/elterninfo/{infobrief}\">Infobrief (PDF)</a>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Programmideen", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/gast/{token}\"", html, StringComparison.Ordinal);
        Statisch(html);

        var emma = await _s.KindAnmeldenAsync(_s.Emma);
        foreach (var ungueltig in new[] { $"/gast/{token}/elterninfo/{emma}", $"/gast/{Zugangslink.NeuesToken()}/elterninfo/{anmeldung}" })
        {
            using var antwort = await SeiteAsync(ungueltig);
            Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
            Assert.Contains("Dieser Link ist nicht mehr gültig.", await antwort.Content.ReadAsStringAsync(Abbruch), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Ohne_Dokumente_fuer_Eltern_steht_Noch_keine_Elterninfo()
    {
        var (token, _, anmeldung) = await _s.GastAnmeldenAsync("lina.eltern@example.org", "Lina");

        var html = await HtmlAsync($"/gast/{token}/elterninfo/{anmeldung}");

        Assert.Contains("Noch keine Elterninfo.", html, StringComparison.Ordinal);
    }

    private static void Statisch(string html)
    {
        Assert.Contains("<meta name=\"robots\" content=\"noindex\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.False(Regex.IsMatch(html, @"\sstyle\s*=", RegexOptions.IgnoreCase), "Kein style-Attribut.");
    }

    private async Task<string> HtmlAsync(string pfad)
    {
        using var antwort = await SeiteAsync(pfad);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return WebUtility.HtmlDecode(await antwort.Content.ReadAsStringAsync(Abbruch));
    }

    private async Task<HttpResponseMessage> SeiteAsync(string pfad)
    {
        using var anfrage = new HttpRequestMessage(HttpMethod.Get, pfad);
        anfrage.Headers.Add(ZeltlotseApiFactory.TestAbsenderIp, "203.0.113.9");
        return await _s.Anonym.SendAsync(anfrage, Abbruch);
    }
}
