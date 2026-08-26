using Zeltlotse.Core.Konten.Contracts;
using Zeltlotse.Core.Persistenz;

namespace Zeltlotse.Server;

public static class MandantMiddleware
{
    /// <summary>
    /// Überträgt die Organisationen aus dem Zugriffstoken in den
    /// Mandantenkontext. Muss nach <c>UseAuthentication</c> laufen und vor
    /// jedem Endpunkt — daraus speist sich die Row-Level-Security.
    /// </summary>
    public static IApplicationBuilder UseMandantAusAnspruechen(this IApplicationBuilder app)
        => app.Use(async (kontext, weiter) =>
        {
            var mandant = kontext.RequestServices.GetRequiredService<MandantKontext>();
            var roh = kontext.User.FindFirst(Ansprueche.Organisationen)?.Value;

            mandant.SichtbareOrganisationen = string.IsNullOrWhiteSpace(roh)
                ? []
                : [.. roh.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(wert => Guid.TryParse(wert, out var id) ? id : Guid.Empty)
                    .Where(id => id != Guid.Empty)];

            mandant.IstBetreiber = kontext.User.IstGlobalAdmin();

            await weiter();
        });
}
