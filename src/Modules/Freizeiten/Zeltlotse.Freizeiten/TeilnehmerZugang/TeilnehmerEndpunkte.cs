using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Freizeiten.Contracts.TeilnehmerZugang;
using Zeltlotse.Freizeiten.Daten;
using Zeltlotse.Freizeiten.Dokumente;
using Zeltlotse.Freizeiten.Domaene;
using Zeltlotse.Organisationen.Contracts.Dokumente;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Dateien;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Rechte;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;
using Zeltlotse.SharedKernel.Zugangslinks;

namespace Zeltlotse.Freizeiten.TeilnehmerZugang;

/// <summary>
/// Story 6-4 (AD-6, AD-11, AD-20): Ob ein QR-Link oder eine Gast-Sitzung gilt. Gesucht wird ohne Mandantenfilter, denn hier sind Token bzw. Gast-Sitzung die
/// Quelle des Mandanten-Kontexts. Gültig ist der Zugang nur, solange der Teilnehmer-Zugang der Freizeit eingeschaltet ist und das Freizeitende (24:00 Uhr
/// Europe/Berlin nach dem letzten Tag) nicht erreicht ist; der Abschluss räumt danach nur noch die Push-Abos ab.
/// </summary>
internal static class QrZugangSuche
{
    /// <summary>Der Link zum Token mit der Freizeit; <c>null</c> für ein unbekanntes, erneuertes, falsch geformtes oder fremdes Token und ohne gültigen Zugang.</summary>
    public static async Task<(QrZugang Zugang, Freizeit Freizeit)?> FindenAsync(
        FreizeitenLeseDbContext db, MandantenKontext mandant, string? token, DateTimeOffset jetzt, CancellationToken abbruch)
    {
        if (!Zugangslink.KannTokenSein(token))
        {
            return null;
        }

        var hash = Zugangslink.TokenHash(QrZugang.Zweck, token!);
        var zugang = await db.QrZugaenge.IgnoreQueryFilters().SingleOrDefaultAsync(q => q.TokenHash == hash, abbruch);
        if (zugang is null)
        {
            return null;
        }

        var freizeit = await db.Freizeiten.IgnoreQueryFilters().SingleOrDefaultAsync(f => f.Id == zugang.FreizeitId, abbruch);
        if (freizeit is null || !Gilt(freizeit, jetzt))
        {
            return null;
        }

        mandant.Setzen(zugang.OrganisationId);
        return (zugang, freizeit);
    }

    /// <summary>Teilnehmer-Zugang an und Freizeitende noch nicht erreicht.</summary>
    public static bool Gilt(Freizeit freizeit, DateTimeOffset jetzt) => freizeit.TeilnehmerZugang && jetzt < FreizeitZeiten.Freizeitende(freizeit.LetzterTag);
}

/// <summary>
/// Story 6-4: <see cref="IQrZugangLesen"/> für die Rechteprüfung des Hosts (ohne Mandantenfilter, nur für den Link mit diesem Stempel) und für den Gast-Push
/// in Kanäle (aktueller Stempel unter dem Mandantenfilter).
/// </summary>
internal sealed class QrZugangLesen(FreizeitenLeseDbContext db, TimeProvider uhr) : IQrZugangLesen
{
    public async Task<Guid?> PruefenAsync(Guid freizeitId, Guid stempel, CancellationToken abbruch = default)
    {
        if (!await db.QrZugaenge.IgnoreQueryFilters().AnyAsync(q => q.FreizeitId == freizeitId && q.Stempel == stempel, abbruch))
        {
            return null;
        }

        var freizeit = await db.Freizeiten.IgnoreQueryFilters().SingleOrDefaultAsync(f => f.Id == freizeitId, abbruch);
        return freizeit is not null && QrZugangSuche.Gilt(freizeit, uhr.GetUtcNow()) ? freizeit.OrganisationId : null;
    }

    public async Task<Guid?> StempelAsync(Guid freizeitId, CancellationToken abbruch = default) =>
        await db.QrZugaenge.Where(q => q.FreizeitId == freizeitId).Select(q => (Guid?)q.Stempel).SingleOrDefaultAsync(abbruch);
}

/// <summary>
/// <c>POST /api/teilnehmer/einloesen</c> (anonym, zugriffsbegrenzt, Antiforgery; Story 6-4, AD-11): tauscht das Token des QR-Links gegen das Gast-Cookie
/// (nur Freizeit, neue Geräte-Kennung und Stempel des Links; dauerhaft bis zum Freizeitende, ohne Gleiten) und antwortet <c>204</c>. Gilt das Token nicht,
/// <c>404</c> und ein Eintrag „Link-Einlösung fehlgeschlagen“ im Sicherheitsprotokoll, ohne Token (AD-22). Der Aufruf des Links selbst zeigt nur die Seite.
/// </summary>
internal static class TeilnehmerEinloesenEndpunkt
{
    public static async Task<IResult> Einloesen(
        TeilnehmerEinloesen koerper, FreizeitenLeseDbContext db, MandantenKontext mandant, ISicherheitsprotokoll protokoll, TimeProvider uhr, HttpContext kontext,
        CancellationToken abbruch)
    {
        var jetzt = uhr.GetUtcNow();
        if (await QrZugangSuche.FindenAsync(db, mandant, koerper.Token, jetzt, abbruch) is not { } gefunden)
        {
            await protokoll.SchreibenAsync(SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen, abbruch: abbruch);
            return TypedResults.NotFound();
        }

        await kontext.SignInAsync(
            GastSitzung.Schema,
            GastSitzung.Prinzipal(gefunden.Freizeit.Id, Kennung.Neu(uhr), gefunden.Zugang.Stempel),
            new AuthenticationProperties
            {
                IsPersistent = true,
                AllowRefresh = false,
                IssuedUtc = jetzt,
                ExpiresUtc = FreizeitZeiten.Freizeitende(gefunden.Freizeit.LetzterTag),
            });
        return TypedResults.NoContent();
    }
}

/// <summary>
/// Endpunkte des QR-Gasts unter <c>/api/teilnehmer</c> (Story 6-4, <c>.NurQrGast()</c>, nur lesen): <c>GET</c> Name und Zeitraum der Freizeit,
/// <c>GET infos</c> die Dokumente mit Sichtbarkeit „Eltern und Teilnehmende“ und <c>GET infos/{dokumentId}</c> deren Datei. Die Freizeit kommt aus der
/// geprüften Gast-Sitzung, nie aus der Adresse; den Mandanten hat der Host gesetzt.
/// </summary>
internal static class TeilnehmerEndpunkte
{
    public static async Task<Results<Ok<TeilnehmerFreizeit>, NotFound>> Laden(IQrGast gast, FreizeitenLeseDbContext db, CancellationToken abbruch) =>
        await db.Freizeiten.Where(f => f.Id == gast.FreizeitId)
                .Select(f => new TeilnehmerFreizeit(f.Name, f.ErsterTag, f.LetzterTag))
                .SingleOrDefaultAsync(abbruch) is { } freizeit
            ? TypedResults.Ok(freizeit)
            : TypedResults.NotFound();

    public static async Task<Ok<TeilnehmerInfos>> Infos(IQrGast gast, FreizeitenLeseDbContext db, IOrgDokumenteLesen org, CancellationToken abbruch)
    {
        var dokumente = await DokumentAnsicht.LesenAsync(Sichtbar(db, gast), org, abbruch);
        return TypedResults.Ok(new TeilnehmerInfos([.. dokumente.Select(d => d.Verweis())]));
    }

    public static async Task<IResult> Datei(
        Guid dokumentId, IQrGast gast, FreizeitenLeseDbContext db, IDateiSpeicher speicher, IOrgDokumenteLesen org, CancellationToken abbruch) =>
        await Sichtbar(db, gast).SingleOrDefaultAsync(d => d.Id == dokumentId, abbruch) is { } dokument
            ? await DokumentAnsicht.DateiAsync(dokument, speicher, org, abbruch)
            : TypedResults.NotFound();

    private static IQueryable<Dokument> Sichtbar(FreizeitenLeseDbContext db, IQrGast gast) =>
        db.Dokumente.Where(d => d.FreizeitId == gast.FreizeitId && d.Sichtbarkeit == DokumentSichtbarkeit.ElternUndTeilnehmende);
}
