using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Playwright;
using Zeltlotse.Testing;

namespace Zeltlotse.E2E.Tests;

/// <summary>
/// Veröffentlicht den API-Host einmal je Lauf (<c>dotnet publish -c Release</c> in einen Temp-Ordner; das Asset-Manifest
/// des Service Workers entsteht nur beim Veröffentlichen) und startet die DLL als Prozess: Umgebung <c>Testing</c>,
/// Kestrel auf <c>https://127.0.0.1:&lt;frei&gt;</c> mit einem je Lauf selbst erzeugten Testzertifikat (Secure-Cookies und
/// Antiforgery verlangen HTTPS, AD-25) und zusätzlich auf <c>http://127.0.0.1:&lt;frei&gt;</c> für die internen Aufrufe der
/// öffentlichen Seiten, PostgreSQL per Testcontainers, Test-Schlüssel, Mail-Adapter <c>Ordner</c> und eingeschaltete
/// Demo-Daten per Umgebungsvariablen. Dazu ein Chromium von Playwright, das dem Testzertifikat vertraut
/// (<c>--ignore-certificate-errors</c>, Kontexte über <see cref="KontextAsync"/>).
/// </summary>
public sealed class VeroeffentlichterHost : IAsyncLifetime
{
    private static readonly TimeSpan Wartezeit = TimeSpan.FromMinutes(10);

    private readonly PostgresContainer _postgres = new();
    private readonly StringBuilder _ausgabe = new();
    private readonly string _ordner = Path.Combine(Path.GetTempPath(), "zeltlotse-e2e-" + Guid.NewGuid().ToString("N"));
    private Process? _host;
    private Uri _httpAdresse = default!;
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
        var httpPort = FreierPort();
        Adresse = new Uri($"https://127.0.0.1:{port}");
        _httpAdresse = new Uri($"http://127.0.0.1:{httpPort}");
        var zertifikat = Path.Combine(_ordner, "e2e-zertifikat.pfx");
        var passwort = Guid.NewGuid().ToString("N");
        await File.WriteAllBytesAsync(zertifikat, TestZertifikat(passwort));
        var start = new ProcessStartInfo("dotnet", [Path.Combine(_ordner, "Zeltlotse.Api.dll")])
        {
            WorkingDirectory = _ordner,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            Environment =
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Testing",
                ["ASPNETCORE_URLS"] = $"{Adresse};{_httpAdresse}",
                ["Kestrel__Certificates__Default__Path"] = zertifikat,
                ["Kestrel__Certificates__Default__Password"] = passwort,
                ["ConnectionStrings__zeltlotse"] = verbindung,
                ["Zeltlotse__Verschluesselung__Hauptschluessel"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                ["Zeltlotse__Protokoll__HmacSchluessel"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                // Doku-Screenshots entstehen aus den erfundenen Demo-Daten (NFR-20).
                ["Zeltlotse__DemoDaten__Aktiv"] = "true",
                // Login-Mails als .eml-Dateien (Ordner-Adapter, nur Testing/Development); Links zeigen auf die HTTPS-Adresse.
                ["Zeltlotse__Mail__Art"] = "Ordner",
                ["Zeltlotse__Mail__Ordner"] = MailOrdnerPfad,
                ["Zeltlotse__OeffentlicheAdresse"] = Adresse.ToString(),
                // Alle Anmeldungen der E2E-Tests kommen von 127.0.0.1; die Grenze prüfen die HTTP-Tests.
                ["Zeltlotse__Zugriffsbegrenzung__AnfragenJeMinute"] = "10000",
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
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            // Selbst erzeugtes Testzertifikat: auch der Service Worker soll registriert werden.
            Args = ["--ignore-certificate-errors"],
        });
    }

    /// <summary>Ordner der Login-Mails des Hosts.</summary>
    public MailOrdner Mails => new(MailOrdnerPfad);

    private string MailOrdnerPfad => Path.Combine(_ordner, "mails");

    /// <summary>Ein Browser-Kontext, der dem Testzertifikat vertraut.</summary>
    public Task<IBrowserContext> KontextAsync(BrowserNewContextOptions optionen)
    {
        optionen.IgnoreHTTPSErrors = true;
        return Browser.NewContextAsync(optionen);
    }

    /// <summary>Selbst signiertes Zertifikat für 127.0.0.1 und localhost, je Lauf neu, als PFX mit Passwort.</summary>
    private static byte[] TestZertifikat(string passwort)
    {
        using var schluessel = RSA.Create(2048);
        var anfrage = new CertificateRequest("CN=127.0.0.1", schluessel, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var namen = new SubjectAlternativeNameBuilder();
        namen.AddIpAddress(IPAddress.Loopback);
        namen.AddDnsName("localhost");
        anfrage.CertificateExtensions.Add(namen.Build());
        anfrage.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], critical: false));
        anfrage.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: false));
        using var zertifikat = anfrage.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        return zertifikat.Export(X509ContentType.Pfx, passwort);
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
        using var client = new HttpClient { BaseAddress = _httpAdresse, Timeout = TimeSpan.FromSeconds(5) };
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

    /// <summary>Wurzel des Repositorys (Ordner mit <c>Zeltlotse.slnx</c>), z. B. für den Bildordner der Doku.</summary>
    internal static string Wurzel()
    {
        var verzeichnis = new DirectoryInfo(AppContext.BaseDirectory);
        while (verzeichnis is not null && !File.Exists(Path.Combine(verzeichnis.FullName, "Zeltlotse.slnx")))
        {
            verzeichnis = verzeichnis.Parent;
        }

        return verzeichnis?.FullName ?? throw new InvalidOperationException("Zeltlotse.slnx nicht gefunden.");
    }
}
