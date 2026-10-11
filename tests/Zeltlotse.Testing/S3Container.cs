using Amazon.Runtime;
using Amazon.S3;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace Zeltlotse.Testing;

/// <summary>
/// Ein S3-kompatibler Dateispeicher (RustFS, gleicher Tag wie im AppHost) je Testassembly (als <c>AssemblyFixture</c> anmelden).
/// Jeder Test, der einen leeren Speicher braucht, nimmt mit <see cref="NeuerBucket"/> einen eigenen Bucket.
/// </summary>
public sealed class S3Container : IAsyncLifetime
{
    /// <summary>Image und fester Tag; im AppHost derselbe Wert.</summary>
    public const string Image = "rustfs/rustfs:1.0.1";

    private const int Port = 9000;
    private const string Zugang = "zeltlotse-test";

    private readonly string _geheimnis = Guid.NewGuid().ToString("N");
    private readonly IContainer _container;

    public S3Container()
    {
        _container = new ContainerBuilder(Image)
            .WithPortBinding(Port, assignRandomHostPort: true)
            .WithEnvironment("RUSTFS_ACCESS_KEY", Zugang)
            .WithEnvironment("RUSTFS_SECRET_KEY", _geheimnis)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(Port).ForPath("/health")))
            .Build();
    }

    public string Endpunkt => $"http://{_container.Hostname}:{_container.GetMappedPublicPort(Port)}";

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Ein neuer, noch nicht angelegter Bucket-Name; der Adapter legt ihn bei Bedarf an.</summary>
    public static string NeuerBucket() => "t-" + Guid.NewGuid().ToString("N")[..20];

    /// <summary>Die Einstellungen <c>Zeltlotse:Dateien:*</c> für den API-Host.</summary>
    public Dictionary<string, string?> Einstellungen(string bucket) => new()
    {
        ["Zeltlotse:Dateien:Endpunkt"] = Endpunkt,
        ["Zeltlotse:Dateien:Bucket"] = bucket,
        ["Zeltlotse:Dateien:Zugang"] = Zugang,
        ["Zeltlotse:Dateien:Geheimnis"] = _geheimnis,
    };

    /// <summary>Roher S3-Client für Prüfungen im Test (z. B. Objekt-Schlüssel im Bucket).</summary>
    public AmazonS3Client RoherClient() => new(
        new BasicAWSCredentials(Zugang, _geheimnis),
        new AmazonS3Config
        {
            ServiceURL = Endpunkt,
            ForcePathStyle = true,
            AuthenticationRegion = "eu-central-1",
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        });
}
