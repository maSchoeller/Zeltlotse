using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Betrieb;
using Zeltlotse.Api.Contracts.Betrieb;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;
using Zeltlotse.SharedKernel.Web;

namespace Zeltlotse.Api.Datenschutz;

/// <summary>Endpunkte des API-Hosts zum Datenschutz (AD-16): die Datenauskunft über alle Module.</summary>
internal static class DatenschutzEndpunkte
{
    public static IEndpointRouteBuilder MapDatenschutzEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/ich/daten", DatenauskunftEndpunkt.Laden).Organisationsuebergreifend().ErneuteAnmeldung();
        return api;
    }
}

/// <summary>
/// <c>GET /api/ich/daten</c> (FR-14, AD-16, AD-25): die Datenauskunft der eigenen Person aus allen <see cref="IDatenauskunft"/> in
/// der Reihenfolge ihrer Registrierung; leere Abschnitte entfallen. Nur mit frischer Anmeldung (höchstens 10 Minuten), sonst
/// <c>401</c>. Jeder Abruf steht als <c>Export</c> im Sicherheitsprotokoll. Der Endpunkt liegt im Host, weil nur er alle Module
/// kennt (AD-2).
/// </summary>
internal static class DatenauskunftEndpunkt
{
    public static async Task<Ok<DatenauskunftAntwort>> Laden(
        IAngemeldet angemeldet, IEnumerable<IDatenauskunft> auskuenfte, ISicherheitsprotokoll protokoll, TimeProvider uhr, CancellationToken abbruch)
    {
        var personId = angemeldet.PersonId ?? throw new InvalidOperationException("Ohne Sitzung erreicht niemand diesen Endpunkt.");
        var abschnitte = new List<DatenauskunftAbschnitt>();
        foreach (var auskunft in auskuenfte)
        {
            foreach (var abschnitt in await auskunft.AuskunftAsync(personId, abbruch))
            {
                if (abschnitt.Zeilen.Count > 0)
                {
                    abschnitte.Add(new DatenauskunftAbschnitt(abschnitt.Titel, [.. abschnitt.Zeilen.Select(z => new DatenauskunftZeile(z.Feld, z.Wert))]));
                }
            }
        }

        await protokoll.SchreibenAsync(SicherheitsEreignisArt.Export, personId, abbruch: abbruch);
        return TypedResults.Ok(new DatenauskunftAntwort(uhr.GetUtcNow(), abschnitte));
    }
}

/// <summary>
/// Datenauskunft des API-Hosts (AD-22): die eigenen Zeilen im Sicherheitsprotokoll mit Art und Zeitpunkt. Das Protokoll bleibt
/// auch nach dem Löschen eines Kontos bestehen (nur Kennungen, feste Frist).
/// </summary>
internal sealed class SicherheitsprotokollAuskunft(IDbContextFactory<BetriebDbContext> kontexte) : IDatenauskunft
{
    public async Task<IReadOnlyList<AuskunftAbschnitt>> AuskunftAsync(Guid personId, CancellationToken abbruch)
    {
        await using var db = await kontexte.CreateDbContextAsync(abbruch);
        var zeilen = await db.Sicherheitsprotokoll.AsNoTracking()
            .Where(z => z.PersonId == personId)
            .OrderBy(z => z.Zeitpunkt)
            .Select(z => new { z.Art, z.Zeitpunkt })
            .ToListAsync(abbruch);
        return [new AuskunftAbschnitt("Sicherheitsprotokoll", [.. zeilen.Select(z => new AuskunftZeile(Art(z.Art), AuskunftFormat.Zeitpunkt(z.Zeitpunkt)))])];
    }

    private static string Art(SicherheitsEreignisArt art) => art switch
    {
        SicherheitsEreignisArt.Anmeldung => "Anmeldung",
        SicherheitsEreignisArt.AnmeldungFehlgeschlagen => "Anmeldung fehlgeschlagen",
        SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen => "Link-Einlösung fehlgeschlagen",
        SicherheitsEreignisArt.Rollenvergabe => "Rollenvergabe",
        SicherheitsEreignisArt.Export => "Datenauskunft",
        SicherheitsEreignisArt.Loeschung => "Löschung",
        SicherheitsEreignisArt.ProfilUebergabe => "Profil-Übergabe",
        _ => art.ToString(),
    };
}
