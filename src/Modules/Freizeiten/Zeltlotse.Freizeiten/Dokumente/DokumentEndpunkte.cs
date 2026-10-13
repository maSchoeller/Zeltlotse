using System.Text.Json;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Freizeiten.Daten;
using Zeltlotse.Freizeiten.Freizeitseite;
using Zeltlotse.Organisationen.Contracts.Dokumente;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Dateien;
using ApiFreizeitDokument = Zeltlotse.Api.Contracts.Freizeiten.FreizeitDokument;

namespace Zeltlotse.Freizeiten.Dokumente;

/// <summary>Ein gelesenes Dokument: die Zeile und, bei einer Einbindung aus Organisationen, Titel, Art, Größe und Datum.</summary>
internal sealed record GelesenesDokument(Dokument Dokument, string Titel, DateiArt Art, long Groesse, DateTimeOffset HochgeladenAm)
{
    public DokumentVerweis Verweis() => new(Dokument.Id, Titel, DokumentDateien.Bezeichnung(Art));

    /// <summary>Keinen Titel ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(GelesenesDokument)}({Dokument.Id})";
}

/// <summary>
/// Lesen der Dokumente einer Freizeit, gemeinsam für Liste, Freizeitseite und Elterninfo (Story 2-7): Einbindungen bekommen Titel, Art,
/// Größe und Datum bei jedem Lesen aus Organisationen (keine Kopie); ist das Org-Dokument gelöscht, fällt die Einbindung weg. Sortiert
/// nach Titel (deutsch, ohne Groß- und Kleinschreibung), dann nach Kennung.
/// </summary>
internal static class DokumentAnsicht
{
    public static async Task<List<GelesenesDokument>> LesenAsync(IQueryable<Dokument> dokumente, IOrgDokumenteLesen org, CancellationToken abbruch)
    {
        var zeilen = await dokumente.ToListAsync(abbruch);
        var eingebunden = zeilen.Where(d => d.OrgDokumentId is not null).Select(d => d.OrgDokumentId!.Value).ToList();
        var infos = eingebunden.Count == 0 ? new Dictionary<Guid, OrgDokumentInfo>() : await org.InfosAsync(eingebunden, abbruch);
        var ergebnis = new List<GelesenesDokument>();
        foreach (var dokument in zeilen)
        {
            if (dokument.OrgDokumentId is { } orgId)
            {
                if (infos.TryGetValue(orgId, out var info))
                {
                    ergebnis.Add(new GelesenesDokument(dokument, info.Titel, info.Dateiart, info.Groesse, info.HochgeladenAm));
                }
            }
            else
            {
                ergebnis.Add(new GelesenesDokument(dokument, dokument.Titel!, dokument.Dateiart!.Value, dokument.Groesse!.Value, dokument.HochgeladenAm));
            }
        }

        return [.. ergebnis.OrderBy(d => d.Titel, DokumentDateien.TitelVergleich).ThenBy(d => d.Dokument.Id)];
    }

    /// <summary>
    /// Die Datei eines Dokuments, immer als Anhang mit dem Titel als Dateinamen: eigene aus dem Dateispeicher, eingebundene über
    /// Organisationen. Fehlt sie (oder das Org-Dokument), <c>404</c>.
    /// </summary>
    public static async Task<IResult> DateiAsync(Dokument dokument, IDateiSpeicher speicher, IOrgDokumenteLesen org, CancellationToken abbruch)
    {
        if (dokument.OrgDokumentId is { } orgId)
        {
            var infos = await org.InfosAsync([orgId], abbruch);
            return infos.TryGetValue(orgId, out var info) && await org.DateiAsync(orgId, abbruch) is { } eingebunden
                ? DateiAuslieferung.Ausliefern(eingebunden.Inhalt, DokumentDateien.Dateiname(info.Titel, eingebunden.Dateiart), immerAnhang: true)
                : TypedResults.NotFound();
        }

        var datei = await speicher.LesenAsync(Dokument.DateiPraefix, dokument.Id, abbruch);
        return datei is not null && DateiPruefung.ArtErkennen(datei.Inhalt) is { } art
            ? DateiAuslieferung.Ausliefern(datei.Inhalt, DokumentDateien.Dateiname(dokument.Titel, art), immerAnhang: true)
            : TypedResults.NotFound();
    }
}

/// <summary>
/// Endpunkte der Dokumente einer Freizeit unter <c>/api/freizeiten/{freizeitId}/dokumente</c> (Leitung und Team,
/// <c>FreizeitRechte.Mitarbeit</c>, Story 2-7):
/// <list type="bullet">
/// <item><c>GET</c> alle Dokumente (alle Sichtbarkeiten, eigene und eingebundene), nach Titel.</item>
/// <item><c>POST</c> Multipart <c>datei</c> und optional <c>sichtbarkeit</c> (Vorgabe Team) → <c>201</c>; Fehler am Feld mit den Texten der Nachweise.</item>
/// <item><c>PATCH …/{dokumentId}</c> genau ein Feld <c>titel</c> oder <c>sichtbarkeit</c> (AD-19) → <c>200 {version, &lt;feld&gt;}</c>.</item>
/// <item><c>DELETE …/{dokumentId}</c> → <c>204</c>; <c>GET …/{dokumentId}/datei</c> als Anhang.</item>
/// <item><c>GET …/org</c> noch nicht eingebundene Org-Dokumente; <c>POST …/einbindungen {orgDokumentId}</c> → <c>201</c>.</item>
/// </list>
/// Nach dem Abschluss ergeben Hochladen, Ändern, Entfernen und Einbinden <c>409 freizeit-abgeschlossen</c>. Unbekannt oder aus einer
/// anderen Freizeit <c>404</c>.
/// </summary>
internal static class DokumentEndpunkte
{
    private static readonly DokumentFeld[] Felder = [DokumentFeld.Titel, DokumentFeld.Sichtbarkeit];

    public static async Task<Ok<FreizeitDokumente>> Laden(Guid freizeitId, FreizeitenLeseDbContext db, IOrgDokumenteLesen org, CancellationToken abbruch)
    {
        var dokumente = await DokumentAnsicht.LesenAsync(db.Dokumente.Where(d => d.FreizeitId == freizeitId), org, abbruch);
        return TypedResults.Ok(new FreizeitDokumente([.. dokumente.Select(ApiForm)]));
    }

    public static async Task<IResult> Hochladen(Guid freizeitId, HttpRequest anfrage, ISender sender, CancellationToken abbruch)
    {
        var formular = anfrage.HasFormContentType ? await anfrage.ReadFormAsync(abbruch) : null;
        var (datei, dateiFehler) = await DokumentDateien.LesenAsync(formular, DokumentFelder.Datei, abbruch);
        var fehler = new Dictionary<string, string[]>();
        if (dateiFehler is not null)
        {
            fehler[DokumentFelder.Datei] = [dateiFehler];
        }

        var sichtbarkeit = DokumentSichtbarkeit.Team;
        if (formular is not null && formular.TryGetValue(DokumentFelder.Sichtbarkeit, out var text))
        {
            if (DokumentRegeln.Sichtbarkeit(text.ToString()) is { } gewaehlt)
            {
                sichtbarkeit = gewaehlt;
            }
            else
            {
                fehler[DokumentFelder.Sichtbarkeit] = [DokumentRegeln.SichtbarkeitUnbekannt];
            }
        }

        if (fehler.Count > 0)
        {
            throw new ValidierungsAusnahme(fehler);
        }

        return await sender.Send(new DokumentHochladenCommand(freizeitId, datei!.Dateiname, sichtbarkeit, datei.Inhalt, datei.Art), abbruch) switch
        {
            DokumentAenderung.Gespeichert gespeichert => TypedResults.Created((string?)null, ApiForm(new GelesenesDokument(
                gespeichert.Dokument, gespeichert.Dokument.Titel!, datei.Art, datei.Inhalt.Length, gespeichert.Dokument.HochgeladenAm))),
            DokumentAenderung.Abgeschlossen => FreizeitseiteProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
    }

    public static async Task<IResult> Aendern(Guid freizeitId, Guid dokumentId, JsonElement koerper, ISender sender, CancellationToken abbruch)
    {
        var (version, alterWert, feld, neuerWert) = Lesen(koerper);
        return await sender.Send(new DokumentFeldAendernCommand(freizeitId, dokumentId, feld, version, alterWert, neuerWert), abbruch) switch
        {
            DokumentAenderung.Gespeichert gespeichert => TypedResults.Ok(new Dictionary<string, object?>
            {
                ["version"] = gespeichert.Dokument.Version,
                [DokumentFelder.JsonName(feld)] = gespeichert.Dokument.Wert(feld),
            }),
            DokumentAenderung.Konflikt => FreizeitseiteProbleme.Konflikt(),
            DokumentAenderung.Abgeschlossen => FreizeitseiteProbleme.Abgeschlossen(),
            DokumentAenderung.NurOrganisation => throw Fehler(DokumentFelder.Titel, "Den Titel pflegt die Organisation."),
            _ => TypedResults.NotFound(),
        };
    }

    public static async Task<IResult> Entfernen(Guid freizeitId, Guid dokumentId, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new DokumentEntfernenCommand(freizeitId, dokumentId), abbruch) switch
        {
            DokumentAenderung.Entfernt => TypedResults.NoContent(),
            DokumentAenderung.Abgeschlossen => FreizeitseiteProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };

    public static async Task<IResult> Datei(
        Guid freizeitId, Guid dokumentId, FreizeitenLeseDbContext db, IDateiSpeicher speicher, IOrgDokumenteLesen org, CancellationToken abbruch)
    {
        var dokument = await db.Dokumente.SingleOrDefaultAsync(d => d.Id == dokumentId && d.FreizeitId == freizeitId, abbruch);
        return dokument is null ? TypedResults.NotFound() : await DokumentAnsicht.DateiAsync(dokument, speicher, org, abbruch);
    }

    public static async Task<Ok<OrgDokumenteAuswahl>> OrgAuswahl(Guid freizeitId, FreizeitenLeseDbContext db, IOrgDokumenteLesen org, CancellationToken abbruch)
    {
        var eingebunden = (await db.Dokumente.Where(d => d.FreizeitId == freizeitId && d.OrgDokumentId != null).Select(d => d.OrgDokumentId!.Value).ToListAsync(abbruch))
            .ToHashSet();
        var alle = await org.ListeAsync(abbruch);
        return TypedResults.Ok(new OrgDokumenteAuswahl([.. alle.Where(d => !eingebunden.Contains(d.Id)).Select(d => new OrgDokumentAuswahl(d.Id, d.Titel))]));
    }

    public static async Task<IResult> Einbinden(Guid freizeitId, DokumentEinbinden anfrage, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new DokumentEinbindenCommand(freizeitId, anfrage.OrgDokumentId), abbruch) switch
        {
            DokumentAenderung.Eingebunden eingebunden => TypedResults.Created((string?)null, ApiForm(new GelesenesDokument(
                eingebunden.Dokument, eingebunden.Info.Titel, eingebunden.Info.Dateiart, eingebunden.Info.Groesse, eingebunden.Info.HochgeladenAm))),
            DokumentAenderung.SchonEingebunden => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Das Org-Dokument ist schon eingebunden.",
                extensions: new Dictionary<string, object?> { [Problemcodes.Feld] = Problemcodes.Konflikt }),
            DokumentAenderung.Abgeschlossen => FreizeitseiteProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };

    private static ApiFreizeitDokument ApiForm(GelesenesDokument d) => new(
        d.Dokument.Id, d.Titel, d.Dokument.Sichtbarkeit, DokumentDateien.Bezeichnung(d.Art), d.Groesse, d.HochgeladenAm, d.Dokument.AusOrganisation, d.Dokument.Version);

    /// <summary>Liest Version, alten Wert und genau ein Feld; jede andere Form ist ein Validierungsfehler (<c>400</c>).</summary>
    private static (uint Version, string? AlterWert, DokumentFeld Feld, string? NeuerWert) Lesen(JsonElement koerper)
    {
        if (koerper.ValueKind != JsonValueKind.Object)
        {
            throw Fehler("version", "Der Körper ist kein JSON-Objekt.");
        }

        if (!koerper.TryGetProperty("version", out var versionWert) || versionWert.ValueKind != JsonValueKind.Number || !versionWert.TryGetUInt32(out var version))
        {
            throw Fehler("version", "Version fehlt.");
        }

        var alterWert = koerper.TryGetProperty("alterWert", out var alt) ? Text(alt, "alterWert") : null;
        var felder = Felder.Where(f => koerper.TryGetProperty(DokumentFelder.JsonName(f), out _)).ToList();
        if (felder.Count != 1)
        {
            throw Fehler("feld", $"Genau ein Feld: {string.Join(", ", Felder.Select(DokumentFelder.JsonName))}.");
        }

        var feld = felder[0];
        var name = DokumentFelder.JsonName(feld);
        return (version, alterWert, feld, Text(koerper.GetProperty(name), name));
    }

    private static string? Text(JsonElement wert, string name) => wert.ValueKind switch
    {
        JsonValueKind.String => wert.GetString(),
        JsonValueKind.Null => null,
        _ => throw Fehler(name, "Nur Text oder null."),
    };

    private static ValidierungsAusnahme Fehler(string feld, string text) => new(new Dictionary<string, string[]> { [feld] = [text] });
}
