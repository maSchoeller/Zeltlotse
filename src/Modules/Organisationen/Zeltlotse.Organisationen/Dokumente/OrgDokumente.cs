using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Organisationen.Contracts.Dokumente;
using Zeltlotse.Organisationen.Daten;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Dateien;
using Zeltlotse.SharedKernel.Mandanten;
using ApiOrgDokument = Zeltlotse.Api.Contracts.Organisationen.OrgDokument;
using ApiOrgDokumente = Zeltlotse.Api.Contracts.Organisationen.OrgDokumente;

namespace Zeltlotse.Organisationen.Dokumente;

/// <summary>
/// Ein Org-Dokument (Story 2-7, FR-43, AD-15): eine Datei der Organisation (Bild oder PDF), die Freizeiten per Kennung einbinden,
/// ohne sie zu kopieren. Die Datei liegt unverschlüsselt im Dateispeicher unter <c>organisationen/&lt;Id&gt;</c>; Titel, Art und Größe
/// stehen hier. Kein Personenbezug: kein Uploader, keine Löschklasse (Arbeitsmaterial der Organisation, keine Personendaten).
/// </summary>
internal sealed class OrgDokument : IOrganisationsEigen
{
    /// <summary>Präfix im Dateispeicher: das Schema des Moduls.</summary>
    public const string DateiPraefix = OrganisationenModul.Schema;

    private OrgDokument()
    {
    }

    public Guid Id { get; private init; }

    public Guid OrganisationId { get; private init; }

    public string Titel { get; private init; } = "";

    public DateiArt Dateiart { get; private init; }

    public long Groesse { get; private init; }

    public DateTimeOffset HochgeladenAm { get; private init; }

    /// <summary>Version der Zeile (<c>xmin</c>, AD-19).</summary>
    public uint Version { get; private set; }

    public static OrgDokument Hochladen(Guid id, Guid organisationId, string titel, DateiArt art, long groesse, DateTimeOffset jetzt) => new()
    {
        Id = id,
        OrganisationId = organisationId,
        Titel = titel,
        Dateiart = art,
        Groesse = groesse,
        HochgeladenAm = jetzt,
    };

    /// <summary>Keinen Titel ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(OrgDokument)}({Id})";
}

/// <summary>
/// Lädt ein Org-Dokument hoch (Org-Admin): die Datei unverschlüsselt in den Dateispeicher, die Zeile mit dem Titel aus dem Dateinamen
/// (<see cref="DokumentDateien.TitelAusDateiname"/>). Typ und Größe hat der Endpunkt am Inhalt geprüft.
/// </summary>
internal sealed record OrgDokumentHochladenCommand(Guid OrganisationId, string? Dateiname, byte[] Inhalt, DateiArt Art)
    : ICommand<OrgDokument>, IModulCommand<OrganisationenDbContext>, IBenutzerCommand
{
    /// <summary>Weder Dateiname noch Inhalt ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(OrgDokumentHochladenCommand)}({OrganisationId})";
}

internal sealed class OrgDokumentHochladenHandler(OrganisationenDbContext db, IDateiSpeicher speicher, TimeProvider uhr)
    : ICommandHandler<OrgDokumentHochladenCommand, OrgDokument>
{
    public async ValueTask<OrgDokument> Handle(OrgDokumentHochladenCommand command, CancellationToken cancellationToken)
    {
        var dokument = OrgDokument.Hochladen(
            Kennung.Neu(uhr), command.OrganisationId, DokumentDateien.TitelAusDateiname(command.Dateiname), command.Art, command.Inhalt.Length, uhr.GetUtcNow());
        await speicher.SpeichernAsync(OrgDokument.DateiPraefix, dokument.Id, command.Inhalt, DateiPruefung.ContentType(command.Art), cancellationToken);
        db.Dokumente.Add(dokument);
        return dokument;
    }
}

/// <summary>
/// Entfernt ein Org-Dokument samt Datei (Org-Admin). Einbindungen in Freizeiten bleiben als Zeile stehen und fallen beim Lesen weg.
/// <c>false</c>, wenn es das Dokument in dieser Organisation nicht gibt.
/// </summary>
internal sealed record OrgDokumentEntfernenCommand(Guid OrganisationId, Guid DokumentId)
    : ICommand<bool>, IModulCommand<OrganisationenDbContext>, IBenutzerCommand;

internal sealed class OrgDokumentEntfernenHandler(OrganisationenDbContext db, IDateiSpeicher speicher) : ICommandHandler<OrgDokumentEntfernenCommand, bool>
{
    public async ValueTask<bool> Handle(OrgDokumentEntfernenCommand command, CancellationToken cancellationToken)
    {
        var dokument = await db.Dokumente.SingleOrDefaultAsync(d => d.Id == command.DokumentId && d.OrganisationId == command.OrganisationId, cancellationToken);
        if (dokument is null)
        {
            return false;
        }

        db.Dokumente.Remove(dokument);
        await speicher.LoeschenAsync(OrgDokument.DateiPraefix, dokument.Id, cancellationToken);
        return true;
    }
}

/// <summary>
/// Endpunkte der Org-Dokumente unter <c>/api/organisationen/{orgId}/dokumente</c> (nur Org-Admin, <see cref="Rollen.OrgRechte.Dokumente"/>):
/// <c>GET</c> nach Titel; <c>POST</c> Multipart-Feld <c>datei</c> → <c>201</c> (Fehler am Feld <c>datei</c> mit den Texten der
/// Nachweise); <c>DELETE …/{dokumentId}</c> → <c>204</c>; <c>GET …/{dokumentId}/datei</c> als Anhang. Unbekannt <c>404</c>.
/// </summary>
internal static class OrgDokumentEndpunkte
{
    /// <summary>Multipart-Feld der Datei, wie bei den Dokumenten der Freizeit und den Nachweisen.</summary>
    private const string DateiFeld = "datei";

    public static async Task<Ok<ApiOrgDokumente>> Laden(OrganisationenLeseDbContext db, CancellationToken abbruch)
    {
        var dokumente = await db.Dokumente.ToListAsync(abbruch);
        return TypedResults.Ok(new ApiOrgDokumente(
        [
            .. dokumente.OrderBy(d => d.Titel, DokumentDateien.TitelVergleich).ThenBy(d => d.Id).Select(ApiForm),
        ]));
    }

    public static async Task<Created<ApiOrgDokument>> Hochladen(Guid orgId, HttpRequest anfrage, ISender sender, CancellationToken abbruch)
    {
        var formular = anfrage.HasFormContentType ? await anfrage.ReadFormAsync(abbruch) : null;
        var (datei, fehler) = await DokumentDateien.LesenAsync(formular, DateiFeld, abbruch);
        if (datei is null)
        {
            throw new ValidierungsAusnahme(new Dictionary<string, string[]> { [DateiFeld] = [fehler!] });
        }

        var dokument = await sender.Send(new OrgDokumentHochladenCommand(orgId, datei.Dateiname, datei.Inhalt, datei.Art), abbruch);
        return TypedResults.Created((string?)null, ApiForm(dokument));
    }

    public static async Task<Results<NoContent, NotFound>> Entfernen(Guid orgId, Guid dokumentId, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new OrgDokumentEntfernenCommand(orgId, dokumentId), abbruch) ? TypedResults.NoContent() : TypedResults.NotFound();

    public static async Task<IResult> Datei(Guid dokumentId, IOrgDokumenteLesen dokumente, CancellationToken abbruch)
    {
        var infos = await dokumente.InfosAsync([dokumentId], abbruch);
        if (!infos.TryGetValue(dokumentId, out var info) || await dokumente.DateiAsync(dokumentId, abbruch) is not { } datei)
        {
            return TypedResults.NotFound();
        }

        return DateiAuslieferung.Ausliefern(datei.Inhalt, DokumentDateien.Dateiname(info.Titel, datei.Dateiart), immerAnhang: true);
    }

    private static ApiOrgDokument ApiForm(OrgDokument d) => new(d.Id, d.Titel, DokumentDateien.Bezeichnung(d.Dateiart), d.Groesse, d.HochgeladenAm);
}

/// <summary>
/// <see cref="IOrgDokumenteLesen"/> über den Lese-Kontext unter dem Mandantenfilter (AD-6): Freizeiten liest nur die Org-Dokumente der
/// Organisation, deren Freizeit die Anfrage betrifft. Die Datei kommt unverschlüsselt aus dem Dateispeicher; ihre Art aus dem Inhalt.
/// </summary>
internal sealed class OrgDokumenteLesen(OrganisationenLeseDbContext db, IDateiSpeicher speicher, IMandantenKontext mandant) : IOrgDokumenteLesen
{
    public async Task<IReadOnlyList<OrgDokumentInfo>> ListeAsync(CancellationToken abbruch = default)
    {
        if (mandant.OrganisationId is null)
        {
            return [];
        }

        var dokumente = await db.Dokumente.ToListAsync(abbruch);
        return [.. dokumente.OrderBy(d => d.Titel, DokumentDateien.TitelVergleich).ThenBy(d => d.Id).Select(Info)];
    }

    public async Task<IReadOnlyDictionary<Guid, OrgDokumentInfo>> InfosAsync(IReadOnlyCollection<Guid> ids, CancellationToken abbruch = default)
    {
        if (mandant.OrganisationId is null || ids.Count == 0)
        {
            return new Dictionary<Guid, OrgDokumentInfo>();
        }

        var liste = ids.Distinct().ToList();
        return (await db.Dokumente.Where(d => liste.Contains(d.Id)).ToListAsync(abbruch)).ToDictionary(d => d.Id, Info);
    }

    public async Task<OrgDokumentDatei?> DateiAsync(Guid id, CancellationToken abbruch = default)
    {
        if (mandant.OrganisationId is null || !await db.Dokumente.AnyAsync(d => d.Id == id, abbruch))
        {
            return null;
        }

        var datei = await speicher.LesenAsync(OrgDokument.DateiPraefix, id, abbruch);
        return datei is not null && DateiPruefung.ArtErkennen(datei.Inhalt) is { } art
            ? new OrgDokumentDatei(datei.Inhalt, DateiPruefung.ContentType(art), art)
            : null;
    }

    private static OrgDokumentInfo Info(OrgDokument d) => new(d.Id, d.Titel, d.Dateiart, d.Groesse, d.HochgeladenAm);
}
