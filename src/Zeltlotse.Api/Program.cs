using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api;
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
using Zeltlotse.SharedKernel.Pipeline;
using Zeltlotse.Sicherheit;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSharedKernel();
builder.Services.AddOutboxZustellDienst();

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

await app.Services.GetRequiredService<ModulMigrationen>().AnwendenAsync();

app.MapDefaultEndpoints();
app.MapZeltlotseEndpoints();

await app.RunAsync();

/// <summary>Einstiegspunkt; öffentlich für <c>WebApplicationFactory</c> in den Tests.</summary>
public partial class Program;
