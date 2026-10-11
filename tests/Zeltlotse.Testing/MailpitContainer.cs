using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace Zeltlotse.Testing;

/// <summary>
/// Mail-Testserver Mailpit (gleicher Tag wie im AppHost) für den SMTP-Adapter: SMTP auf Port 1025 ohne Anmeldung,
/// die empfangenen Mails über die HTTP-API auf Port 8025. Als Klassen-Fixture anmelden.
/// </summary>
public sealed class MailpitContainer : IAsyncLifetime
{
    /// <summary>Image und fester Tag; im AppHost derselbe Wert.</summary>
    public const string Image = "axllent/mailpit:v1.31.5";

    private const int SmtpPort = 1025;
    private const int WebPort = 8025;

    private readonly IContainer _container = new ContainerBuilder(Image)
        .WithPortBinding(SmtpPort, assignRandomHostPort: true)
        .WithPortBinding(WebPort, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(WebPort).ForPath("/api/v1/info")))
        .Build();

    public string Host => _container.Hostname;

    public int Smtp => _container.GetMappedPublicPort(SmtpPort);

    /// <summary>Basisadresse der HTTP-API (<c>/api/v1/…</c>).</summary>
    public Uri Web => new($"http://{_container.Hostname}:{_container.GetMappedPublicPort(WebPort)}");

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
