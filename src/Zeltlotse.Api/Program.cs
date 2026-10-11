using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api;
using Zeltlotse.Api.Adapter;
using Zeltlotse.Api.Betrieb;
using Zeltlotse.Api.Fehler;
using Zeltlotse.Api.Sicherheit;
using Zeltlotse.Api.Start;
using Zeltlotse.Finanzen;
using Zeltlotse.Freizeiten;
using Zeltlotse.Inventar;
using Zeltlotse.Kanaele;
using Zeltlotse.Konten;
using Zeltlotse.Kueche;
using Zeltlotse.Organisationen;
using Zeltlotse.Personen;
using Zeltlotse.Programm;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Pipeline;
using Zeltlotse.Sicherheit;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSharedKernel();
builder.Services.AddOutboxZustellDienst();
builder.Services.AddProtokollSchwaerzung();
builder.Services.AddFehlerform();
builder.Services.AddWebSicherheit();
builder.Services.AddBetrieb();
builder.Services.AddDateiSpeicher();
builder.Services.AddSingleton(new DbKontextVerzeichnis(builder.Services));

// Genau ein AddMediator, Scoped, Pipeline-Reihenfolge nach AD-27:
// Validierung -> Inbox (nur Ereignis-Empfänger) -> Transaktion -> Outbox.
builder.Services.AddMediator(options =>
{
    options.Namespace = "Zeltlotse.Api.Mediator";
    options.GenerateTypesAsInternal = true;
    options.ServiceLifetime = ServiceLifetime.Scoped;
    options.Assemblies =
    [
        typeof(KontenModul),
        typeof(PersonenModul),
        typeof(OrganisationenModul),
        typeof(FreizeitenModul),
        typeof(SicherheitModul),
        typeof(ProgrammModul),
        typeof(InventarModul),
        typeof(KuecheModul),
        typeof(FinanzenModul),
        typeof(KanaeleModul),
    ];
    options.PipelineBehaviors =
    [
        typeof(ValidierungsStufe<,>),
        typeof(InboxStufe<,>),
        typeof(TransaktionsStufe<,>),
        typeof(OutboxStufe<,>),
    ];
});

builder.Services.AddZeltlotseModule();
builder.Services.AddSingleton<ModulMigrationen>();

var app = builder.Build();

Startpruefung.Ausfuehren(app.Services, app.Environment);
await app.Services.GetRequiredService<ModulMigrationen>().AnwendenAsync();

// Reihenfolge (AD-22, AD-25): Header und Proxy-Auswertung zuerst, dann Fehlerform, Routing,
// Cookie-Richtlinie, Zugriffsbegrenzung und zuletzt der Antiforgery-Zwang vor den Endpunkten.
app.UseWebSicherheitAmAnfang();
app.UseFehlerform();
app.UseRouting();
app.UseWebSicherheitNachRouting();

app.MapDefaultEndpoints();
var api = app.MapZeltlotseEndpoints();
if (app.Environment.IsEnvironment(Startpruefung.TestUmgebung))
{
    foreach (var zusatz in app.Services.GetServices<IZusatzEndpunkte>())
    {
        zusatz.Abbilden(api);
    }
}

await app.RunAsync();

/// <summary>Einstiegspunkt; öffentlich für <c>WebApplicationFactory</c> in den Tests.</summary>
public partial class Program;
