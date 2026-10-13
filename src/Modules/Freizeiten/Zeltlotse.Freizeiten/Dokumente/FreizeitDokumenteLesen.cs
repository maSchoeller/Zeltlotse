using Zeltlotse.Freizeiten.Contracts.Dokumente;
using Zeltlotse.Freizeiten.Daten;
using Zeltlotse.Organisationen.Contracts.Dokumente;
using Zeltlotse.SharedKernel.Dateien;

namespace Zeltlotse.Freizeiten.Dokumente;

/// <summary>
/// Story 2-9: <see cref="IFreizeitDokumenteLesen"/> über den Lese-Kontext unter dem Mandantenfilter. Liest wie die Liste über
/// <see cref="DokumentAnsicht.LesenAsync"/>: Einbindungen bekommen Titel und Art aus Organisationen, ein gelöschtes Org-Dokument fällt weg.
/// </summary>
internal sealed class FreizeitDokumenteLesen(FreizeitenLeseDbContext db, IOrgDokumenteLesen org) : IFreizeitDokumenteLesen
{
    public async Task<FreizeitDokumentInfo?> InfoAsync(Guid freizeitId, Guid dokumentId, CancellationToken abbruch = default)
    {
        var dokumente = await DokumentAnsicht.LesenAsync(db.Dokumente.Where(d => d.FreizeitId == freizeitId && d.Id == dokumentId), org, abbruch);
        return dokumente is [var dokument] ? new FreizeitDokumentInfo(dokument.Dokument.Id, dokument.Titel, DokumentDateien.Bezeichnung(dokument.Art)) : null;
    }
}
