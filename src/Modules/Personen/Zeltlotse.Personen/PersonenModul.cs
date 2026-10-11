using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Personen.Daten;
using Zeltlotse.Personen.Profil;
using Zeltlotse.Personen.Schluessel;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Web;

namespace Zeltlotse.Personen;

/// <summary>Einzige öffentliche Oberfläche des Moduls: Registrierung und Endpunkte (AD-27).</summary>
public static class PersonenModul
{
    internal const string Name = "Personen";

    internal const string Schema = "personen";

    public static IServiceCollection AddPersonenModul(this IServiceCollection services)
    {
        services.AddModul<PersonenDbContext, PersonenLeseDbContext>(Name, Schema);
        services.AddScoped<IPersonenSchluesselQuelle, PersonenSchluesselQuelle>();
        return services;
    }

    /// <summary>Bindet die Endpunkte des Moduls in die <c>/api</c>-Gruppe ein; das eigene Profil liegt unter <c>/api/ich/profil</c> (AD-26).</summary>
    public static IEndpointRouteBuilder MapPersonenEndpoints(this IEndpointRouteBuilder api)
    {
        var profil = api.MapGroup("/ich/profil").Organisationsuebergreifend();
        profil.MapGet("", ProfilLadenEndpunkt.Laden);
        profil.MapPost("", ProfilSicherstellenEndpunkt.Sicherstellen);
        profil.MapPatch("", ProfilFeldAendernEndpunkt.Aendern);
        return api;
    }
}
