using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Freizeiten.Daten;
using Zeltlotse.Freizeiten.Domaene;
using Zeltlotse.Freizeiten.Freizeitseite;
using Zeltlotse.Organisationen.Contracts.Dokumente;
using Zeltlotse.Personen.Contracts.Anmeldung;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Dateien;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Freizeiten.Dokumente;

/// <summary>
/// Die eine Zugriffsregel der Elterninfo (Story 2-7, FR-68, AD-6, AD-26): Die Anmeldung gehört einer erlaubten Person und ist nicht
/// abgesagt. Gelesen wird die Anmeldung ohne Mandantenfilter, aber nur die geprüfte (wie <c>AnmeldungenDerPersonen</c>); danach setzt
/// die Regel den Mandanten genau einmal auf die Organisation ihrer Freizeit, bevor Organisationen gelesen wird. Geliefert werden nur
/// Dokumente dieser Freizeit mit Sichtbarkeit <see cref="DokumentSichtbarkeit.ElternUndTeilnehmende"/>; sonst <c>404</c>.
/// </summary>
internal static class ElterninfoZugriff
{
    private sealed record GepruefteAnmeldung(Guid FreizeitId, string FreizeitName);

    public static async Task<Results<Ok<Elterninfo>, NotFound>> LadenAsync(
        IReadOnlyCollection<Guid> personen, Guid anmeldungId, FreizeitenLeseDbContext db, MandantenKontext mandant, IOrgDokumenteLesen org, CancellationToken abbruch)
    {
        if (await PruefenAsync(personen, anmeldungId, db, mandant, abbruch) is not { } anmeldung)
        {
            return TypedResults.NotFound();
        }

        var dokumente = await DokumentAnsicht.LesenAsync(Sichtbar(db, anmeldung.FreizeitId), org, abbruch);
        return TypedResults.Ok(new Elterninfo(anmeldung.FreizeitName, [.. dokumente.Select(d => d.Verweis())]));
    }

    public static async Task<IResult> DateiAsync(
        IReadOnlyCollection<Guid> personen, Guid anmeldungId, Guid dokumentId, FreizeitenLeseDbContext db, MandantenKontext mandant, IDateiSpeicher speicher,
        IOrgDokumenteLesen org, CancellationToken abbruch)
    {
        if (await PruefenAsync(personen, anmeldungId, db, mandant, abbruch) is not { } anmeldung
            || await Sichtbar(db, anmeldung.FreizeitId).SingleOrDefaultAsync(d => d.Id == dokumentId, abbruch) is not { } dokument)
        {
            return TypedResults.NotFound();
        }

        return await DokumentAnsicht.DateiAsync(dokument, speicher, org, abbruch);
    }

    private static IQueryable<Dokument> Sichtbar(FreizeitenLeseDbContext db, Guid freizeitId) =>
        db.Dokumente.Where(d => d.FreizeitId == freizeitId && d.Sichtbarkeit == DokumentSichtbarkeit.ElternUndTeilnehmende);

    private static async Task<GepruefteAnmeldung?> PruefenAsync(
        IReadOnlyCollection<Guid> personen, Guid anmeldungId, FreizeitenLeseDbContext db, MandantenKontext mandant, CancellationToken abbruch)
    {
        var ids = personen.ToList();
        var freizeitId = await db.Anmeldungen.IgnoreQueryFilters()
            .Where(a => a.Id == anmeldungId && ids.Contains(a.PersonId) && a.Status != AnmeldeStatus.Abgesagt)
            .Select(a => (Guid?)a.FreizeitId)
            .SingleOrDefaultAsync(abbruch);
        if (freizeitId is null)
        {
            return null;
        }

        var freizeit = await db.Freizeiten.IgnoreQueryFilters()
            .Where(f => f.Id == freizeitId)
            .Select(f => new { f.Id, f.Name, f.OrganisationId })
            .SingleOrDefaultAsync(abbruch);
        if (freizeit is null)
        {
            return null;
        }

        mandant.Setzen(freizeit.OrganisationId);
        return new GepruefteAnmeldung(freizeit.Id, freizeit.Name);
    }
}

/// <summary>
/// <c>GET /api/freizeiten/gast/{gastToken}/anmeldungen/{anmeldungId}/elterninfo</c> und <c>…/elterninfo/{dokumentId}</c> (anonym,
/// zugriffsbegrenzt, AD-11): die Elterninfo über den Gast-Link für eine Anmeldung der Gast-Familie. Gilt der Gast-Link nicht (mehr),
/// <c>404</c>.
/// </summary>
internal static class ElterninfoGastEndpunkt
{
    public static async Task<Results<Ok<Elterninfo>, NotFound>> Laden(
        string gastToken, Guid anmeldungId, IGastZugaenge gaeste, FreizeitenLeseDbContext db, MandantenKontext mandant, IOrgDokumenteLesen org, CancellationToken abbruch) =>
        await gaeste.PruefenAsync(gastToken, abbruch) is { } gast
            ? await ElterninfoZugriff.LadenAsync([gast.ElternPersonId, .. gast.KindPersonIds], anmeldungId, db, mandant, org, abbruch)
            : TypedResults.NotFound();

    public static async Task<IResult> Datei(
        string gastToken, Guid anmeldungId, Guid dokumentId, IGastZugaenge gaeste, FreizeitenLeseDbContext db, MandantenKontext mandant, IDateiSpeicher speicher,
        IOrgDokumenteLesen org, CancellationToken abbruch) =>
        await gaeste.PruefenAsync(gastToken, abbruch) is { } gast
            ? await ElterninfoZugriff.DateiAsync([gast.ElternPersonId, .. gast.KindPersonIds], anmeldungId, dokumentId, db, mandant, speicher, org, abbruch)
            : TypedResults.NotFound();
}

/// <summary>
/// <c>GET /api/ich/anmeldungen/{anmeldungId}/elterninfo</c> und <c>…/elterninfo/{dokumentId}</c> (AD-26, organisationsübergreifend): die
/// Elterninfo einer Anmeldung der angemeldeten Person oder eines Kindes ihrer Familie (<see cref="IVertretung"/>).
/// </summary>
internal static class ElterninfoKontoEndpunkt
{
    public static async Task<Results<Ok<Elterninfo>, NotFound>> Laden(
        Guid anmeldungId, IAngemeldet angemeldet, IVertretung vertretung, FreizeitenLeseDbContext db, MandantenKontext mandant, IOrgDokumenteLesen org, CancellationToken abbruch) =>
        await ElterninfoZugriff.LadenAsync(await PersonenAsync(angemeldet, vertretung, abbruch), anmeldungId, db, mandant, org, abbruch);

    public static async Task<IResult> Datei(
        Guid anmeldungId, Guid dokumentId, IAngemeldet angemeldet, IVertretung vertretung, FreizeitenLeseDbContext db, MandantenKontext mandant, IDateiSpeicher speicher,
        IOrgDokumenteLesen org, CancellationToken abbruch) =>
        await ElterninfoZugriff.DateiAsync(await PersonenAsync(angemeldet, vertretung, abbruch), anmeldungId, dokumentId, db, mandant, speicher, org, abbruch);

    private static async Task<IReadOnlyList<Guid>> PersonenAsync(IAngemeldet angemeldet, IVertretung vertretung, CancellationToken abbruch) =>
        await vertretung.PersonenAsync(angemeldet.PersonId ?? throw new InvalidOperationException("Ohne Sitzung erreicht niemand diesen Endpunkt."), abbruch);
}

/// <summary>
/// <c>GET /api/freizeiten/oeffentlich/{token}/dokumente/{dokumentId}</c> (anonym, zugriffsbegrenzt, AD-11): die Datei eines Dokuments
/// mit Sichtbarkeit <see cref="DokumentSichtbarkeit.Oeffentlich"/> der Freizeit des Anmeldelinks. Mandant aus dem Token
/// (<see cref="AnmeldelinkSuche"/>). Ungültiger Link, anderes Dokument oder andere Sichtbarkeit <c>404</c>.
/// </summary>
internal static class OeffentlichesDokumentEndpunkt
{
    public static async Task<IResult> Datei(
        string token, Guid dokumentId, FreizeitenLeseDbContext db, MandantenKontext mandant, IDateiSpeicher speicher, IOrgDokumenteLesen org, TimeProvider uhr,
        CancellationToken abbruch)
    {
        if (await AnmeldelinkSuche.FindenAsync(db, mandant, token, uhr.GetUtcNow(), abbruch) is not { } gefunden
            || await Oeffentlich(db, gefunden.Freizeit.Id).SingleOrDefaultAsync(d => d.Id == dokumentId, abbruch) is not { } dokument)
        {
            return TypedResults.NotFound();
        }

        return await DokumentAnsicht.DateiAsync(dokument, speicher, org, abbruch);
    }

    /// <summary>Die öffentlichen Dokumente einer Freizeit als Verweise für die Freizeitseite, nach Titel.</summary>
    public static async Task<List<DokumentVerweis>> VerweiseAsync(FreizeitenLeseDbContext db, Freizeit freizeit, IOrgDokumenteLesen org, CancellationToken abbruch) =>
        [.. (await DokumentAnsicht.LesenAsync(Oeffentlich(db, freizeit.Id), org, abbruch)).Select(d => d.Verweis())];

    private static IQueryable<Dokument> Oeffentlich(FreizeitenLeseDbContext db, Guid freizeitId) =>
        db.Dokumente.Where(d => d.FreizeitId == freizeitId && d.Sichtbarkeit == DokumentSichtbarkeit.Oeffentlich);
}
