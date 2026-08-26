using Microsoft.Playwright;

// Erzeugt die Bilder für user-docs/. Reproduzierbar: gleiche Seedaten, gleiche
// Fenstergrößen, gleiche Reihenfolge — zweimal gestartet kommt zweimal
// dasselbe heraus.
//
//   ./run-local.ps1            (in einem anderen Fenster)
//   dotnet run --project tools/Zeltlotse.Screenshots

var adresse = args.FirstOrDefault() ?? "http://localhost:5299";
var ziel = args.Skip(1).FirstOrDefault()
    ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "user-docs", "bilder");

ziel = Path.GetFullPath(ziel);
Directory.CreateDirectory(ziel);

Console.WriteLine($"Oberfläche: {adresse}");
Console.WriteLine($"Bilder:     {ziel}");

Microsoft.Playwright.Program.Main(["install", "chromium"]);

using var playwright = await Playwright.CreateAsync();
// --lang muss beim Start stehen: Native Datumsfelder richten ihr Format nach
// der Browsersprache, nicht nach der Kultur der Seite. Ohne das zeigen die
// Bilder mm/dd/yyyy, obwohl ein deutscher Browser TT.MM.JJJJ anzeigt.
await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
{
    Args = ["--lang=de-DE"],
});

var kontext = await browser.NewContextAsync(new BrowserNewContextOptions
{
    ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
    Locale = "de-DE",
    DeviceScaleFactor = 2,
});

var seite = await kontext.NewPageAsync();

await AufnehmenAsync(seite, "/anmelden", "anmelden");

// Anmeldung als Organisationsleitung — die Rolle mit den meisten Bildschirmen.
await seite.FillAsync("#email", "leitung@zeltlotse.local");
await seite.FillAsync("#kennwort", "Entwicklung!1");
await seite.ClickAsync("button[type=submit]");
await seite.WaitForURLAdresse(adresse);

await AufnehmenAsync(seite, "/", "meine-freizeiten");
await AufnehmenAsync(seite, "/o/ev-kirchengemeinde-musterstadt", "freizeiten");

// Der Dialog gehört zum Bildschirm, nicht daneben.
await seite.ClickAsync("text=Freizeit anlegen");
await seite.WaitForSelectorAsync(".zl-dialog");
await SchreibenAsync(seite, ziel, "freizeit-anlegen");

await seite.Keyboard.PressAsync("Escape");

await AufnehmenAsync(seite, "/o/ev-kirchengemeinde-musterstadt/team", "mitglieder");
await AufnehmenAsync(seite, "/o/ev-kirchengemeinde-musterstadt/papierkorb", "papierkorb");

// Schmale Breite: dieselbe Ansicht, anderes Gerät.
var mobil = await browser.NewContextAsync(new BrowserNewContextOptions
{
    ViewportSize = new ViewportSize { Width = 375, Height = 812 },
    Locale = "de-DE",
    DeviceScaleFactor = 2,
    IsMobile = true,
    HasTouch = true,
});

var kleineSeite = await mobil.NewPageAsync();
await kleineSeite.GotoAsync($"{adresse}/anmelden");
await kleineSeite.FillAsync("#email", "leitung@zeltlotse.local");
await kleineSeite.FillAsync("#kennwort", "Entwicklung!1");
await kleineSeite.ClickAsync("button[type=submit]");
await kleineSeite.WaitForURLAdresse(adresse);

await SchreibenAsync(kleineSeite, ziel, "meine-freizeiten-schmal");

Console.WriteLine("Fertig.");

async Task AufnehmenAsync(IPage s, string pfad, string name)
{
    await s.GotoAsync($"{adresse.TrimEnd('/')}{pfad}");
    await s.WaitForSelectorAsync("h1");
    await SchreibenAsync(s, ziel, name);
}

static async Task SchreibenAsync(IPage seite, string ziel, string name)
{
    // Kurz warten, damit Platzhalter durch echte Inhalte ersetzt sind.
    await seite.WaitForTimeoutAsync(400);

    var datei = Path.Combine(ziel, $"{name}.png");
    await seite.ScreenshotAsync(new PageScreenshotOptions { Path = datei });

    Console.WriteLine($"  {name}.png");
}

internal static class SeitenErweiterungen
{
    /// <summary>Wartet, bis die Anwendung die Startseite anzeigt.</summary>
    public static Task WaitForURLAdresse(this IPage seite, string adresse)
        => seite.WaitForURLAsync($"{adresse.TrimEnd('/')}/", new PageWaitForURLOptions
        {
            Timeout = 15_000,
        });
}
