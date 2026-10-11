using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Konten.Anmelden;
using Zeltlotse.Konten.Contracts.Sitzungen;
using Zeltlotse.Konten.Daten;
using Zeltlotse.Konten.Domaene;
using Zeltlotse.Konten.Ich;
using Zeltlotse.Konten.Passwort;
using Zeltlotse.Konten.Sitzungen;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Web;

namespace Zeltlotse.Konten;

/// <summary>Einzige öffentliche Oberfläche des Moduls: Registrierung und Endpunkte (AD-27).</summary>
public static class KontenModul
{
    internal const string Name = "Konten";

    internal const string Schema = "konten";

    public static IServiceCollection AddKontenModul(this IServiceCollection services)
    {
        services.AddModul<KontenDbContext, KontenLeseDbContext>(Name, Schema);
        services.AddSingleton<IPasswordHasher<Konto>>(_ => PasswortRegel.NeuerHasher());
        services.AddScoped<IKontoPruefung, KontoPruefung>();
        return services;
    }

    /// <summary>
    /// Bindet die Endpunkte des Moduls in die <c>/api</c>-Gruppe ein. Die vier Anmelde-Endpunkte sind anonym und
    /// zugriffsbegrenzt (AD-11, AD-25); alle anderen verlangen eine Sitzung (Vorgabe der <c>/api</c>-Gruppe).
    /// </summary>
    public static IEndpointRouteBuilder MapKontenEndpoints(this IEndpointRouteBuilder api)
    {
        var konten = api.MapGroup("/konten");
        konten.MapPost("/login-link", LoginLinkAnfordernEndpunkt.Anfordern).AllowAnonymous().Zugriffsbegrenzt();
        konten.MapPost("/login-link/einloesen", LoginLinkEinloesenEndpunkt.Einloesen).AllowAnonymous().Zugriffsbegrenzt();
        konten.MapPost("/login-code/einloesen", LoginCodeEinloesenEndpunkt.Einloesen).AllowAnonymous().Zugriffsbegrenzt();
        konten.MapPost("/anmelden", PasswortAnmeldenEndpunkt.Anmelden).AllowAnonymous().Zugriffsbegrenzt();
        konten.MapPost("/abmelden", AbmeldenEndpunkt.Abmelden);

        var ich = api.MapGroup("/ich/konto").Organisationsuebergreifend();
        ich.MapGet("", KontoLadenEndpunkt.Laden);
        ich.MapPut("/passwort", PasswortFestlegenEndpunkt.Festlegen);
        return api;
    }
}
