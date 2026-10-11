// Lokal startet Aspire alles (AD-23): PostgreSQL 18, den S3-kompatiblen Dateispeicher und den API-Host.
var builder = DistributedApplication.CreateBuilder(args);

// Geheimnisse (AD-14, AD-22): beim ersten Start erzeugt und in den User Secrets des AppHost gespeichert,
// damit verschlüsselte Daten und Log-HMACs über Neustarts lesbar bzw. vergleichbar bleiben. Nie im Repository.
// 43 Zeichen aus dem Base64-Alphabet plus "=" ergeben genau 32 Bytes (Hauptschlüssel) bzw. mindestens 32 Bytes (HMAC).
var base64Schluessel = new GenerateParameterDefault { MinLength = 43, Lower = true, Upper = true, Numeric = true, Special = false };
var hauptschluessel = builder.AddParameter("hauptschluessel", base64Schluessel, secret: true, persist: true);
var hmacSchluessel = builder.AddParameter("protokoll-hmac", base64Schluessel, secret: true, persist: true);

var dateienZugang = builder.AddParameter(
    "dateien-zugang", new GenerateParameterDefault { MinLength = 20, Special = false }, secret: true, persist: true);
var dateienGeheimnis = builder.AddParameter(
    "dateien-geheimnis", new GenerateParameterDefault { MinLength = 40, Special = false }, secret: true, persist: true);

var postgres = builder.AddPostgres("postgres")
    .WithImageTag("18.6")
    .WithDataVolume();

var datenbank = postgres.AddDatabase("zeltlotse");

// S3-kompatibler Dateispeicher (AD-15, AD-23): RustFS mit festem Tag, derselbe wie in den Tests (S3Container).
var dateien = builder.AddContainer("dateien", "rustfs/rustfs", "1.0.1")
    .WithHttpEndpoint(targetPort: 9000, name: "s3")
    .WithEnvironment("RUSTFS_ACCESS_KEY", dateienZugang)
    .WithEnvironment("RUSTFS_SECRET_KEY", dateienGeheimnis)
    .WithVolume("zeltlotse-dateien", "/data")
    .WithHttpHealthCheck("/health", endpointName: "s3");

// Profil https: Alle Cookies sind Secure (AD-25), das Antiforgery-Cookie wird nur über HTTPS ausgegeben.
builder.AddProject<Projects.Zeltlotse_Api>("api", launchProfileName: "https")
    .WithReference(datenbank)
    .WaitFor(datenbank)
    .WaitFor(dateien)
    .WithEnvironment("Zeltlotse__Verschluesselung__Hauptschluessel", ReferenceExpression.Create($"{hauptschluessel}="))
    .WithEnvironment("Zeltlotse__Protokoll__HmacSchluessel", ReferenceExpression.Create($"{hmacSchluessel}="))
    .WithEnvironment("Zeltlotse__Dateien__Endpunkt", dateien.GetEndpoint("s3"))
    .WithEnvironment("Zeltlotse__Dateien__Bucket", "zeltlotse")
    .WithEnvironment("Zeltlotse__Dateien__Zugang", dateienZugang)
    .WithEnvironment("Zeltlotse__Dateien__Geheimnis", dateienGeheimnis)
    .WithHttpHealthCheck("/health");

builder.Build().Run();
