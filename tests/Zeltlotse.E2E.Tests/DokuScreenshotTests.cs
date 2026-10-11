using System.Buffers.Binary;
using Microsoft.Playwright;

namespace Zeltlotse.E2E.Tests;

/// <summary>
/// Doku-Screenshots (AD-30, NFR-20): Jede heutige Oberfläche in Handy- (375×812) und Laptop-Größe (1280×800), hell,
/// <c>de-DE</c>, <c>Europe/Berlin</c>, gegen den Host mit Demo-Daten. Die Bilder landen als <c>&lt;name&gt;-&lt;gerät&gt;.png</c>
/// unter <c>doku/src/assets/screenshots/</c> und werden bei jedem Lauf überschrieben. Vor dem Bild wird der sichtbare
/// Inhalt geprüft, danach Dateiformat und Breite (kein Pixelvergleich).
/// Gezielt: <c>dotnet test --project tests/Zeltlotse.E2E.Tests -- --filter-trait "Kategorie=Doku"</c>.
/// </summary>
[Trait("Kategorie", "Doku")]
public sealed class DokuScreenshotTests(VeroeffentlichterHost host)
{
    private static readonly byte[] PngSignatur = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static TheoryData<string> Geraete => ["handy", "laptop"];

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task App_Start(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await seite.GotoAsync(new Uri(host.Adresse, "/app/").ToString());
        await Assertions.Expect(seite.GetByText("Noch keine Freizeit geöffnet.")).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Zeltlotse");
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await BildAsync(seite, "app-start", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task App_Offline(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        // Einmal online laden, bis der Service Worker die Seite steuert; dann offline neu laden (wie OfflineStartTests).
        await seite.GotoAsync(new Uri(host.Adresse, "/app/").ToString());
        await Assertions.Expect(seite.GetByText("Noch keine Freizeit geöffnet.")).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await seite.EvaluateAsync("() => navigator.serviceWorker.ready");
        await seite.ReloadAsync();
        await seite.WaitForFunctionAsync("() => navigator.serviceWorker.controller !== null", null, new() { Timeout = 60_000 });
        await Assertions.Expect(seite.GetByText("Noch keine Freizeit geöffnet.")).ToBeVisibleAsync(new() { Timeout = 60_000 });

        await kontext.SetOfflineAsync(true);
        await seite.ReloadAsync();

        await Assertions.Expect(seite.Locator(".zl-offline-leiste")).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(seite.Locator(".zl-notfall-box a[href='tel:112']")).ToHaveTextAsync("Notruf 112");
        await Assertions.Expect(seite.GetByText("Offline nicht verfügbar.")).ToBeVisibleAsync();

        await BildAsync(seite, "app-offline", geraet);
    }

    [Theory]
    [MemberData(nameof(Geraete))]
    public async Task Oeffentliche_Startseite(string geraet)
    {
        await using var kontext = await KontextAsync(geraet);
        var seite = await kontext.NewPageAsync();

        await seite.GotoAsync(new Uri(host.Adresse, "/").ToString());
        await Assertions.Expect(seite.Locator("h1")).ToHaveTextAsync("Zeltlotse", new() { Timeout = 60_000 });
        await seite.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await BildAsync(seite, "oeffentliche-startseite", geraet);
    }

    private static (int Breite, int Hoehe) Groesse(string geraet) => geraet switch
    {
        "handy" => (375, 812),
        "laptop" => (1280, 800),
        _ => throw new ArgumentOutOfRangeException(nameof(geraet), geraet, "Unbekanntes Gerät."),
    };

    private Task<IBrowserContext> KontextAsync(string geraet)
    {
        var (breite, hoehe) = Groesse(geraet);
        return host.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = breite, Height = hoehe },
            DeviceScaleFactor = 1,
            ColorScheme = ColorScheme.Light,
            Locale = "de-DE",
            TimezoneId = "Europe/Berlin",
        });
    }

    /// <summary>Schreibt das Bild des sichtbaren Bereichs und prüft danach PNG-Signatur und Breite aus dem IHDR-Block.</summary>
    private static async Task BildAsync(IPage seite, string name, string geraet)
    {
        var ordner = Path.Combine(VeroeffentlichterHost.Wurzel(), "doku", "src", "assets", "screenshots");
        Directory.CreateDirectory(ordner);
        var pfad = Path.Combine(ordner, $"{name}-{geraet}.png");
        var vorher = DateTime.UtcNow.AddSeconds(-1);

        await seite.ScreenshotAsync(new PageScreenshotOptions { Path = pfad, Type = ScreenshotType.Png, Animations = ScreenshotAnimations.Disabled });

        var bytes = await File.ReadAllBytesAsync(pfad, TestContext.Current.CancellationToken);
        Assert.True(File.GetLastWriteTimeUtc(pfad) >= vorher, $"{pfad} wurde nicht neu geschrieben.");
        Assert.True(bytes.Length > 24 && bytes.AsSpan(0, 8).SequenceEqual(PngSignatur), $"{pfad} ist kein PNG.");
        Assert.Equal(Groesse(geraet).Breite, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)));
    }
}
