using System.Net;
using Amazon.S3.Model;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.SharedKernel.Dateien;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>AD-15: der S3-Adapter im API-Host gegen einen echten S3-kompatiblen Speicher.</summary>
public sealed class S3DateiSpeicherTests(PostgresContainer postgres, S3Container s3)
{
    private static readonly byte[] Pdf = [.. "%PDF-1.7\n"u8, 1, 2, 3, 4];

    private async Task<(ZeltlotseApiFactory Factory, string Bucket)> FactoryAsync()
    {
        var bucket = S3Container.NeuerBucket();
        var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync());
        foreach (var (schluessel, wert) in s3.Einstellungen(bucket))
        {
            factory.Einstellungen[schluessel] = wert;
        }

        return (factory, bucket);
    }

    [Fact]
    public async Task Speichern_lesen_und_loeschen()
    {
        var (factory, _) = await FactoryAsync();
        await using var _f = factory;
        var speicher = factory.Services.GetRequiredService<IDateiSpeicher>();
        var kennung = Guid.CreateVersion7();

        await speicher.SpeichernAsync("personen", kennung, Pdf, "application/pdf", Abbruch);
        var gelesen = await speicher.LesenAsync("personen", kennung, Abbruch);

        Assert.NotNull(gelesen);
        Assert.Equal(Pdf, gelesen.Inhalt);
        Assert.Equal("application/pdf", gelesen.ContentType);

        await speicher.LoeschenAsync("personen", kennung, Abbruch);

        Assert.Null(await speicher.LesenAsync("personen", kennung, Abbruch));
    }

    [Fact]
    public async Task Objekt_liegt_unter_Modul_Praefix_und_Kennung_und_Praefixe_trennen()
    {
        var (factory, bucket) = await FactoryAsync();
        await using var _f = factory;
        var speicher = factory.Services.GetRequiredService<IDateiSpeicher>();
        var kennung = Guid.CreateVersion7();

        await speicher.SpeichernAsync("freizeiten", kennung, Pdf, "application/pdf", Abbruch);

        using var roh = s3.RoherClient();
        var liste = await roh.ListObjectsV2Async(new ListObjectsV2Request { BucketName = bucket }, Abbruch);
        Assert.Equal([$"freizeiten/{kennung}"], liste.S3Objects.Select(o => o.Key));
        Assert.Null(await speicher.LesenAsync("personen", kennung, Abbruch));
    }

    [Fact]
    public async Task Bucket_ist_nicht_oeffentlich_lesbar()
    {
        var (factory, bucket) = await FactoryAsync();
        await using var _f = factory;
        var kennung = Guid.CreateVersion7();
        await factory.Services.GetRequiredService<IDateiSpeicher>().SpeichernAsync("personen", kennung, Pdf, "application/pdf", Abbruch);

        using var anonym = new HttpClient();
        using var antwort = await anonym.GetAsync($"{s3.Endpunkt}/{bucket}/personen/{kennung}", Abbruch);

        // Genau „verboten“: ein 301/400 (z. B. falscher Pfadstil) bewiese nicht, dass der Bucket privat ist.
        Assert.Equal(HttpStatusCode.Forbidden, antwort.StatusCode);
    }

    [Fact]
    public async Task Loeschen_einer_fehlenden_Datei_ist_kein_Fehler()
    {
        var (factory, _) = await FactoryAsync();
        await using var _f = factory;

        await factory.Services.GetRequiredService<IDateiSpeicher>().LoeschenAsync("personen", Guid.CreateVersion7(), Abbruch);
    }

    [Fact]
    public async Task Ungueltiger_Praefix_wird_abgelehnt()
    {
        var (factory, _) = await FactoryAsync();
        await using var _f = factory;

        await Assert.ThrowsAsync<ArgumentException>(() => factory.Services.GetRequiredService<IDateiSpeicher>()
            .SpeichernAsync("../konten", Guid.CreateVersion7(), Pdf, "application/pdf", Abbruch));
    }

    [Fact]
    public async Task Ohne_Konfiguration_meldet_der_Speicher_was_fehlt()
    {
        await using var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync());

        var fehler = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.Services.GetRequiredService<IDateiSpeicher>()
            .LesenAsync("personen", Guid.CreateVersion7(), Abbruch));

        Assert.Contains("Zeltlotse:Dateien", fehler.Message, StringComparison.Ordinal);
    }
}
