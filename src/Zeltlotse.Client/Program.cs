using System.Globalization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Zeltlotse.Client;
using Zeltlotse.Client.Dienste;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Einsprachig deutsch — Datumsangaben sollen aussehen wie im Kalender.
var kultur = new CultureInfo("de-DE");
CultureInfo.DefaultThreadCurrentCulture = kultur;
CultureInfo.DefaultThreadCurrentUICulture = kultur;

var api = builder.Configuration["Api"] ?? builder.HostEnvironment.BaseAddress;

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(api) });
builder.Services.AddScoped<Sitzung>();
builder.Services.AddScoped<ZeltlotseApi>();

await builder.Build().RunAsync();
