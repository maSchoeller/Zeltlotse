using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Freizeiten.Daten;
using Zeltlotse.Freizeiten.Domaene;
using Zeltlotse.Personen.Contracts.Anmeldung;
using Zeltlotse.Personen.Contracts.Namen;
using Zeltlotse.SharedKernel.Anmeldung;
using ApiArt = Zeltlotse.Api.Contracts.Freizeiten.AnmeldungArt;
using ApiStatus = Zeltlotse.Api.Contracts.Freizeiten.AnmeldungStatus;

namespace Zeltlotse.Freizeiten.Anmeldungen;

/// <summary>Übersetzung der gespeicherten Art und des Status in die Werte der API.</summary>
internal static class AnmeldungWerte
{
    public static ApiArt Art(AnmeldeArt art) => art switch
    {
        AnmeldeArt.Teilnehmende => ApiArt.Teilnehmende,
        AnmeldeArt.Mitarbeitende => ApiArt.Mitarbeitende,
        _ => throw new ArgumentOutOfRangeException(nameof(art), art, null),
    };

    public static ApiStatus Status(AnmeldeStatus status) => status switch
    {
        AnmeldeStatus.Eingegangen => ApiStatus.Eingegangen,
        AnmeldeStatus.Bestaetigt => ApiStatus.Bestaetigt,
        AnmeldeStatus.Abgesagt => ApiStatus.Abgesagt,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}

/// <summary>Eine Anmeldung einer Person mit den Übersichtsfeldern ihrer Freizeit und dem Kontakt der Leitung für Absagen (FR-21).</summary>
internal sealed record AnmeldungDerPerson(
    Guid AnmeldungId, Guid PersonId, string FreizeitName, DateOnly ErsterTag, DateOnly LetzterTag, AnmeldeArt Art, AnmeldeStatus Status, string? KontaktMail, string? KontaktTelefon);

/// <summary>
/// Liest Anmeldungen einer Menge von Personen über Organisationen hinweg (personenbezogener Lesepfad, AD-26): ohne Mandantenfilter,
/// aber nur für die übergebenen Personen, und nur Übersichtsfelder mit Name, Zeitraum und Kontakt für Absagen der Freizeit.
/// </summary>
internal static class AnmeldungenDerPersonen
{
    public static async Task<IReadOnlyList<AnmeldungDerPerson>> LesenAsync(
        FreizeitenLeseDbContext db, IReadOnlyCollection<Guid> personIds, CancellationToken abbruch)
    {
        var ids = personIds.ToList();
        var anmeldungen = await db.Anmeldungen.IgnoreQueryFilters()
            .Where(a => ids.Contains(a.PersonId))
            .Select(a => new { a.Id, a.PersonId, a.FreizeitId, a.Art, a.Status })
            .ToListAsync(abbruch);
        var freizeitIds = anmeldungen.Select(a => a.FreizeitId).Distinct().ToList();
        var freizeiten = await db.Freizeiten.IgnoreQueryFilters()
            .Where(f => freizeitIds.Contains(f.Id))
            .Select(f => new { f.Id, f.Name, f.ErsterTag, f.LetzterTag, f.KontaktMail, f.KontaktTelefon })
            .ToDictionaryAsync(f => f.Id, abbruch);
        return
        [
            .. anmeldungen
                .Select(a => (a.Id, a.PersonId, Freizeit: freizeiten[a.FreizeitId], a.Art, a.Status))
                .OrderByDescending(a => a.Freizeit.ErsterTag)
                .ThenBy(a => a.Freizeit.Name, StringComparer.CurrentCulture)
                .ThenBy(a => a.Id)
                .Select(a => new AnmeldungDerPerson(
                    a.Id, a.PersonId, a.Freizeit.Name, a.Freizeit.ErsterTag, a.Freizeit.LetzterTag, a.Art, a.Status, a.Freizeit.KontaktMail, a.Freizeit.KontaktTelefon)),
        ];
    }
}

/// <summary>
/// <c>GET /api/ich/anmeldungen</c> (AD-26, organisationsübergreifend): die Anmeldungen der angemeldeten Person und der Kinder ihrer
/// Familie (<see cref="IVertretung"/>), nur Übersichtsfelder.
/// </summary>
internal static class MeineAnmeldungenEndpunkt
{
    public static async Task<Ok<List<AnmeldungZeile>>> Laden(IAngemeldet angemeldet, IVertretung vertretung, FreizeitenLeseDbContext db, CancellationToken abbruch)
    {
        var personId = angemeldet.PersonId ?? throw new InvalidOperationException("Ohne Sitzung erreicht niemand diesen Endpunkt.");
        var zeilen = await AnmeldungenDerPersonen.LesenAsync(db, await vertretung.PersonenAsync(personId, abbruch), abbruch);
        return TypedResults.Ok(zeilen
            .Select(z => new AnmeldungZeile(
                z.AnmeldungId, z.PersonId, z.FreizeitName, z.ErsterTag, z.LetzterTag, AnmeldungWerte.Art(z.Art), AnmeldungWerte.Status(z.Status), z.KontaktMail, z.KontaktTelefon))
            .ToList());
    }
}

/// <summary>
/// <c>GET /api/freizeiten/gast/{gastToken}</c> (anonym, zugriffsbegrenzt, AD-11): die Anmeldungen der Gast-Familie mit Vorname,
/// Freizeit, Zeitraum und Status und das Ende des Gast-Links. Ungültig, abgelaufen, übernommen oder anderer Zweck <c>404</c>. Nur
/// eine Ansicht, darum kein Eintrag im Sicherheitsprotokoll.
/// </summary>
internal static class GastLinkEndpunkt
{
    public static async Task<Results<Ok<GastLinkAnsicht>, NotFound>> Laden(
        string gastToken, IGastZugaenge gaeste, IPersonenNamen namen, FreizeitenLeseDbContext db, CancellationToken abbruch)
    {
        if (await gaeste.PruefenAsync(gastToken, abbruch) is not { } gast)
        {
            return TypedResults.NotFound();
        }

        var zeilen = await AnmeldungenDerPersonen.LesenAsync(db, [gast.ElternPersonId, .. gast.KindPersonIds], abbruch);
        var vornamen = await namen.NamenAsync([.. zeilen.Select(z => z.PersonId).Distinct()], abbruch);
        return TypedResults.Ok(new GastLinkAnsicht(
            gast.GueltigBis,
            [
                .. zeilen.Select(z => new GastLinkAnmeldung(
                    vornamen.GetValueOrDefault(z.PersonId)?.Vorname ?? "", z.FreizeitName, z.ErsterTag, z.LetzterTag, AnmeldungWerte.Status(z.Status),
                    z.KontaktMail, z.KontaktTelefon)),
            ]));
    }
}
