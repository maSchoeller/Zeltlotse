using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Zeltlotse.SharedKernel.Datenschutz;

namespace Zeltlotse.SharedKernel.Tests;

/// <summary>Matrix-Zeile „Log mit Personendaten“: nur der HMAC-Wert, nie der Klartext (AD-22).</summary>
public sealed partial class SchwaerzungTests
{
    private const string Name = "Erika Muster";

    [Fact]
    public void Personenbezogener_Parameter_erscheint_nur_als_HMAC()
    {
        using var dienste = Dienste(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        var sammler = dienste.GetFakeLogCollector();
        var logger = dienste.GetRequiredService<ILogger<SchwaerzungTests>>();
        var personId = Guid.CreateVersion7();

        PersonGemeldet(logger, personId, Name);

        var eintrag = Assert.Single(sammler.GetSnapshot());
        Assert.DoesNotContain("Erika", eintrag.Message, StringComparison.Ordinal);
        Assert.Contains(personId.ToString(), eintrag.Message, StringComparison.Ordinal);
        var werte = eintrag.StructuredState!.ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.DoesNotContain(werte.Values, w => w is not null && w.Contains("Erika", StringComparison.Ordinal));
        var geschwaerzt = werte.Single(kv => kv.Key.Equals("name", StringComparison.OrdinalIgnoreCase)).Value;
        Assert.False(string.IsNullOrEmpty(geschwaerzt));
        Assert.NotEqual(Name, geschwaerzt);
    }

    [Fact]
    public void Gleicher_Wert_ergibt_gleichen_HMAC_damit_Eintraege_zuordenbar_bleiben()
    {
        using var dienste = Dienste(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        var sammler = dienste.GetFakeLogCollector();
        var logger = dienste.GetRequiredService<ILogger<SchwaerzungTests>>();

        PersonGemeldet(logger, Guid.Empty, Name);
        PersonGemeldet(logger, Guid.Empty, Name);

        var nachrichten = sammler.GetSnapshot().Select(e => e.Message).ToList();
        Assert.Equal(nachrichten[0], nachrichten[1]);
    }

    [Fact]
    public void Nicht_kanonisches_Base64_wie_aus_Aspire_wird_angenommen()
    {
        // 43 Zeichen aus dem Base64-Alphabet plus "=": gesetzte Füllbits im letzten Zeichen (so erzeugt der AppHost den Schlüssel).
        using var dienste = Dienste(string.Concat(Enumerable.Repeat("Zz9", 14)) + "B=");
        var sammler = dienste.GetFakeLogCollector();

        PersonGemeldet(dienste.GetRequiredService<ILogger<SchwaerzungTests>>(), Guid.Empty, Name);

        Assert.DoesNotContain("Erika", Assert.Single(sammler.GetSnapshot()).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("kurz")]
    public void Fehlender_oder_zu_kurzer_HMAC_Schluessel_bricht_ab_ohne_den_Wert_zu_nennen(string? wert)
    {
        var konfiguration = Konfiguration(wert);

        var fehler = Assert.Throws<InvalidOperationException>(() => ProtokollSchwaerzung.SchluesselPruefen(konfiguration));

        Assert.Contains(ProtokollSchwaerzung.Konfigurationsschluessel, fehler.Message, StringComparison.Ordinal);
        if (!string.IsNullOrEmpty(wert))
        {
            Assert.DoesNotContain(wert, fehler.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Fehlender_Schluessel_laesst_auch_die_Redaktor_Optionen_scheitern()
    {
        using var dienste = Dienste(null);

        Assert.ThrowsAny<Exception>(() =>
        {
            var logger = dienste.GetRequiredService<ILogger<SchwaerzungTests>>();
            PersonGemeldet(logger, Guid.Empty, Name);
        });
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Person {PersonId} gemeldet: {Name}")]
    private static partial void PersonGemeldet(ILogger logger, Guid personId, [Personenbezogen] string name);

    private static ServiceProvider Dienste(string? schluessel)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Konfiguration(schluessel));
        services.AddLogging(b => b.AddFakeLogging());
        services.AddProtokollSchwaerzung();
        return services.BuildServiceProvider();
    }

    private static IConfiguration Konfiguration(string? schluessel) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [ProtokollSchwaerzung.Konfigurationsschluessel] = schluessel })
            .Build();
}
