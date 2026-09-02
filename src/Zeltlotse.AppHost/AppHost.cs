var builder = DistributedApplication.CreateBuilder(args);

var datenbank = builder.AddSqlServer("sqlserver")
    .WithDataVolume()
    .AddDatabase("zeltlotse");

var server = builder.AddProject<Projects.Zeltlotse_Server>("server", launchProfileName: "http")
    .WithReference(datenbank)
    .WaitFor(datenbank);

var client = builder.AddProject<Projects.Zeltlotse_Client>("client", launchProfileName: "http")
    .WithReference(server)
    .WaitFor(server);

// Der Server muss den Ursprung der Oberfläche kennen: für CORS mit
// Anmeldeinformationen und für die Adresse in den Einladungslinks.
server.WithEnvironment("Zeltlotse__ClientUrsprung", client.GetEndpoint("http"));

builder.Build().Run();
