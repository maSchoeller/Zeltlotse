using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Playwright;
using Zeltlotse.Testing;

namespace Zeltlotse.E2E.Tests;

/// <summary>
/// Veröffentlicht den API-Host einmal je Lauf (<c>dotnet publish -c Release</c> in einen Temp-Ordner; das Asset-Manifest
/// des Service Workers entsteht nur beim Veröffentlichen) und startet die DLL als Prozess: Umgebung <c>Testing</c>,
/// Kestrel auf <c>http://127.0.0.1:&lt;frei&gt;</c>, PostgreSQL per Testcontainers, Test-Schlüssel per Umgebungsvariablen.
/// Dazu ein Chromium von Playwright.
/// </summary>
public sealed class VeroeffentlichterHost : IAsyncLifetime
{
    private static readonly TimeSpan Wartezeit = TimeSpan.FromMinutes(10);

    private readonly PostgresContainer _postgres = new();
    private readonly StringBuilder _ausgabe = new();
    private readonly string _ordner = Path.Combine(Path.GetTempPath(), "zeltlotse-e2e-" + Guid.NewGuid().ToString("N"));
    private Process? _host;
    private IPlaywright? _playwright;

    public Uri Adresse { get; private set; } = default!;

    public IBrowser Browser { get; private set; } = default!;

    public async ValueTask InitializeAsync()
    {
        var veroeffentlichen = VeroeffentlichenAsync();
        await _postgres.InitializeAsync();
        var verbindung = await _postgres.NeueDatenbankAsync();
        await veroeffentlichen;

        var port = FreierPort();
        Adresse = new Uri($"http://127.0.0.1:{port}");
        var start = new ProcessStartInfo("dotnet", [Path.Combine(_ordner, "Zeltlotse.Api.dll")])
        {
            WorkingDirectory = _ordner,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            Environment =
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Testing",
                ["ASPNETCORE_URLS"] = Adresse.ToString(),
                ["ConnectionStrings__zeltlotse"] = verbindung,
                ["Zeltlotse__Verschluesselung__Hauptschluessel"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                ["Zeltlotse__Protokoll__HmacSchluessel"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            },
        };
        _host = Process.Start(start) ?? throw new InvalidOperationException("Der Host ließ sich nicht starten.");
        _host.OutputDataReceived += (_, e) => Mitschreiben(e.Data);
        _host.ErrorDataReceived += (_, e) => Mitschreiben(e.Data);
        _host.BeginOutputReadLine();
        _host.BeginErrorReadLine();
        await BereitAsync();

        var installiert = Microsoft.Playwright.Program.Main(["install", "chromium"]);
        if (installiert != 0)
        {
            throw new InvalidOperationException($"Chromium für Playwright ließ sich nicht installieren (Code {installiert}).");
        }

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async ValueTask DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }

        _playwright?.Dispose();
        if (_host is { HasExited: false })
        {
            _host.Kill(entireProcessTree: true);
            await _host.WaitForExitAsync();
        }

        _host?.Dispose();
        await _postgres.DisposeAsync();
        try
        {
            Directory.Delete(_ordner, recursive: true);
        }
        catch (IOException)
        {
            // Der Temp-Ordner darf liegen bleiben, falls eine Datei noch gesperrt ist.
        }
        catch (UnauthorizedAccessException)
        {
            // Ebenso.
        }
    }

    private async Task VeroeffentlichenAsync()
    {
        var projekt = Path.Combine(Wurzel(), "src", "Zeltlotse.Api", "Zeltlotse.Api.csproj");
        var start = new ProcessStartInfo("dotnet", ["publish", projekt, "-c", "Release", "-o", _ordner, "--nologo"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var prozess = Process.Start(start) ?? throw new InvalidOperationException("dotnet publish ließ sich nicht starten.");
        var ausgabe = prozess.StandardOutput.ReadToEndAsync();
        var fehler = prozess.StandardError.ReadToEndAsync();
        using var abbruch = new CancellationTokenSource(Wartezeit);
        await prozess.WaitForExitAsync(abbruch.Token);
        if (prozess.ExitCode != 0)
        {
            throw new InvalidOperationException($"dotnet publish ist gescheitert:\n{await ausgabe}\n{await fehler}");
        }
    }

    private async Task BereitAsync()
    {
        using var client = new HttpClient { BaseAddress = Adresse, Timeout = TimeSpan.FromSeconds(5) };
        var ende = DateTime.UtcNow + TimeSpan.FromMinutes(2);
        while (DateTime.UtcNow < ende)
        {
            if (_host!.HasExited)
            {
                throw new InvalidOperationException($"Der Host hat sich beendet (Code {_host.ExitCode}):\n{Ausgabe}");
            }

            try
            {
                using var antwort = await client.GetAsync("/health");
                if (antwort.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Noch nicht bereit.
            }
            catch (TaskCanceledException)
            {
                // Noch nicht bereit.
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"Der Host wurde nicht bereit:\n{Ausgabe}");
    }

    /// <summary>Die bisherige Ausgabe des Hosts, für Fehlermeldungen.</summary>
    public string Ausgabe
    {
        get
        {
            lock (_ausgabe)
            {
                return _ausgabe.ToString();
            }
        }
    }

    private void Mitschreiben(string? zeile)
    {
        if (zeile is null)
        {
            return;
        }

        lock (_ausgabe)
        {
            _ausgabe.AppendLine(zeile);
        }
    }

    private static int FreierPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string Wurzel()
    {
        var verzeichnis = new DirectoryInfo(AppContext.BaseDirectory);
        while (verzeichnis is not null && !File.Exists(Path.Combine(verzeichnis.FullName, "Zeltlotse.slnx")))
        {
            verzeichnis = verzeichnis.Parent;
        }

        return verzeichnis?.FullName ?? throw new InvalidOperationException("Zeltlotse.slnx nicht gefunden.");
    }
}
