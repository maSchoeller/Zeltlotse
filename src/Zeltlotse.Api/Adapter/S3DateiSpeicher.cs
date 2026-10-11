using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Microsoft.Extensions.Options;
using Zeltlotse.SharedKernel.Dateien;

namespace Zeltlotse.Api.Adapter;

/// <summary>Verbindung zum S3-kompatiblen Dateispeicher, Abschnitt <c>Zeltlotse:Dateien</c> (Geheimnisse aus der Geheimnisverwaltung).</summary>
internal sealed class DateiSpeicherOptionen
{
    public const string Abschnitt = "Zeltlotse:Dateien";

    public string? Endpunkt { get; set; }

    public string? Bucket { get; set; }

    public string? Zugang { get; set; }

    public string? Geheimnis { get; set; }

    public string Region { get; set; } = "eu-central-1";

    public bool Vollstaendig =>
        !string.IsNullOrWhiteSpace(Endpunkt) && !string.IsNullOrWhiteSpace(Bucket)
        && !string.IsNullOrWhiteSpace(Zugang) && !string.IsNullOrWhiteSpace(Geheimnis);
}

/// <summary>
/// S3-Adapter für <see cref="IDateiSpeicher"/> (AD-15, AD-23): der einzige Ort mit S3-Code. Objekte liegen unter
/// <c>&lt;modul-präfix&gt;/&lt;kennung&gt;</c> in einem privaten Bucket; URLs auf Objekte werden nie herausgegeben.
/// </summary>
internal sealed class S3DateiSpeicher(IOptions<DateiSpeicherOptionen> optionen) : IDateiSpeicher, IDisposable
{
    private readonly SemaphoreSlim _bucketSperre = new(1, 1);
    private AmazonS3Client? _client;
    private bool _bucketBereit;

    public async Task SpeichernAsync(string modulPraefix, Guid kennung, byte[] inhalt, string contentType, CancellationToken abbruch = default)
    {
        var schluessel = DateiSchluessel.Bilden(modulPraefix, kennung);
        var client = await ClientAsync(abbruch);
        using var strom = new MemoryStream(inhalt, writable: false);
        await client.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = optionen.Value.Bucket,
                Key = schluessel,
                InputStream = strom,
                ContentType = contentType,
                UseChunkEncoding = false,
            },
            abbruch);
    }

    public async Task<GespeicherteDatei?> LesenAsync(string modulPraefix, Guid kennung, CancellationToken abbruch = default)
    {
        var schluessel = DateiSchluessel.Bilden(modulPraefix, kennung);
        var client = await ClientAsync(abbruch);
        try
        {
            using var antwort = await client.GetObjectAsync(optionen.Value.Bucket, schluessel, abbruch);
            using var inhalt = new MemoryStream();
            await antwort.ResponseStream.CopyToAsync(inhalt, abbruch);
            return new GespeicherteDatei(inhalt.ToArray(), antwort.Headers.ContentType);
        }
        catch (AmazonS3Exception fehler) when (fehler.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task LoeschenAsync(string modulPraefix, Guid kennung, CancellationToken abbruch = default)
    {
        var schluessel = DateiSchluessel.Bilden(modulPraefix, kennung);
        var client = await ClientAsync(abbruch);
        await client.DeleteObjectAsync(optionen.Value.Bucket, schluessel, abbruch);
    }

    public void Dispose()
    {
        _client?.Dispose();
        _bucketSperre.Dispose();
    }

    private async Task<AmazonS3Client> ClientAsync(CancellationToken abbruch)
    {
        if (_bucketBereit)
        {
            return _client!;
        }

        await _bucketSperre.WaitAsync(abbruch);
        try
        {
            if (_bucketBereit)
            {
                return _client!;
            }

            var o = optionen.Value;
            if (!o.Vollstaendig)
            {
                throw new InvalidOperationException(
                    $"Der Dateispeicher ist nicht konfiguriert ({DateiSpeicherOptionen.Abschnitt}:Endpunkt, Bucket, Zugang, Geheimnis).");
            }

            _client ??= new AmazonS3Client(
                new BasicAWSCredentials(o.Zugang, o.Geheimnis),
                new AmazonS3Config
                {
                    ServiceURL = o.Endpunkt,
                    ForcePathStyle = true,
                    AuthenticationRegion = o.Region,
                    // Viele S3-kompatible Speicher kennen die neuen Standard-Prüfsummen des SDK v4 nicht.
                    RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                    ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
                });

            if (!await AmazonS3Util.DoesS3BucketExistV2Async(_client, o.Bucket))
            {
                await _client.PutBucketAsync(new PutBucketRequest { BucketName = o.Bucket }, abbruch);
            }

            _bucketBereit = true;
            return _client;
        }
        finally
        {
            _bucketSperre.Release();
        }
    }
}

internal static class DateiSpeicherRegistrierung
{
    public static IServiceCollection AddDateiSpeicher(this IServiceCollection services)
    {
        services.AddOptions<DateiSpeicherOptionen>().BindConfiguration(DateiSpeicherOptionen.Abschnitt);
        services.AddSingleton<IDateiSpeicher, S3DateiSpeicher>();
        return services;
    }
}
