// Lokal startet Aspire alles (AD-23): PostgreSQL 18 und den API-Host.
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithImageTag("18.6")
    .WithDataVolume();

var datenbank = postgres.AddDatabase("zeltlotse");

builder.AddProject<Projects.Zeltlotse_Api>("api")
    .WithReference(datenbank)
    .WaitFor(datenbank)
    .WithHttpHealthCheck("/health");

builder.Build().Run();
