using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Zeltlotse.Core.Freizeiten;
using Zeltlotse.Core.Konten;
using Zeltlotse.Core.Konten.Contracts;
using Zeltlotse.Core.Organisationen;
using Zeltlotse.Core.Persistenz;
using Zeltlotse.Server;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var token = builder.Configuration.GetSection(TokenEinstellungen.Abschnitt).Get<TokenEinstellungen>()
    ?? new TokenEinstellungen();

if (string.IsNullOrWhiteSpace(token.Schluessel))
{
    // Ohne gesetzten Schlüssel läuft nur die Entwicklung — produktiv kommt er
    // aus dem Key Vault und das Hochfahren scheitert bewusst, wenn er fehlt.
    token.Schluessel = builder.Environment.IsDevelopment()
        ? "entwicklung-nur-lokal-mindestens-32-zeichen!"
        : throw new InvalidOperationException(
            $"{TokenEinstellungen.Abschnitt}:Schluessel ist nicht gesetzt.");
}

var einladung = builder.Configuration.GetSection(EinladungsEinstellungen.Abschnitt)
    .Get<EinladungsEinstellungen>() ?? new EinladungsEinstellungen();

var clientUrsprung = builder.Configuration["Zeltlotse:ClientUrsprung"];

if (!string.IsNullOrWhiteSpace(clientUrsprung))
{
    einladung.Basisadresse = clientUrsprung;
}

builder.Services.AddSingleton(token);
builder.Services.AddSingleton(einladung);

builder.Services.AddDbContext<ZeltlotseDbContext>((dienste, optionen) =>
{
    optionen.UseNpgsql(builder.Configuration.GetConnectionString("zeltlotse"));
    optionen.AddInterceptors(dienste.GetRequiredService<MandantInterceptor>());
});

builder.Services
    .AddPersistenz()
    .AddKonten()
    .AddOrganisationen()
    .AddFreizeiten();

builder.Services.AddHostedService<Aufraeumdienst>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = token.Aussteller,
            ValidAudience = token.Empfaenger,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(token.Schluessel)),
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddCors(o => o.AddDefaultPolicy(regel =>
{
    if (string.IsNullOrWhiteSpace(clientUrsprung))
    {
        return;
    }

    regel.WithOrigins(clientUrsprung)
        .AllowAnyHeader()
        .AllowAnyMethod()
        // Nötig, damit das Erneuerungs-Cookie mitreist.
        .AllowCredentials();
}));

var app = builder.Build();

app.MapDefaultEndpoints();

await Startvorgang.VorbereitenAsync(app);

app.UseCors();
app.UseAuthentication();
app.UseMandantAusAnspruechen();
app.UseAuthorization();

app.MapKonten();
app.MapOrganisationen();
app.MapFreizeiten();

app.Run();

/// <summary>Für die Integrationstests sichtbar.</summary>
public partial class Program;
