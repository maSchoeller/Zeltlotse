using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Finanzen;
using Zeltlotse.Finanzen.Daten;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Dateien;
using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Finanzen.Belege;

/// <summary>
/// Story 5-2 (FR-58, AD-15): Lädt Foto oder PDF zu einem vorhandenen Beleg hoch. Getrennt vom Anlegen, damit ein gescheiterter Upload den Beleg nicht
/// verliert; ein neuer Upload ersetzt die alte Datei. Die Datei liegt unverschlüsselt im Dateispeicher unter <c>finanzen/&lt;BelegId&gt;</c>. Typ und Größe
/// hat der Endpunkt am Inhalt geprüft. Eine abgeschlossene Freizeit ist nur lesbar (AD-17).
/// </summary>
internal sealed record BelegDateiHochladenCommand(Guid FreizeitId, Guid BelegId, byte[] Inhalt, DateiArt Art, string? Dateiname)
    : ICommand<BelegAenderung>, IModulCommand<FinanzenDbContext>, IBenutzerCommand, ISchalterCommand
{
    public FunktionsSchalter Schalter => FunktionsSchalter.Finanzen;

    /// <summary>Weder Inhalt noch Dateiname ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(BelegDateiHochladenCommand)}({FreizeitId}, {BelegId}, {Art})";
}

internal sealed class BelegDateiHochladenHandler(FinanzenDbContext db, IFreizeitZeitpunkte zeitpunkte, IDateiSpeicher speicher)
    : ICommandHandler<BelegDateiHochladenCommand, BelegAenderung>
{
    /// <summary>Titel der Datei, wenn der Browser keinen Dateinamen nennt.</summary>
    private const string StandardTitel = "Beleg";

    public async ValueTask<BelegAenderung> Handle(BelegDateiHochladenCommand command, CancellationToken cancellationToken)
    {
        if (await BelegSchritte.FreizeitPruefenAsync(zeitpunkte, command.FreizeitId, cancellationToken) is { } abbruch)
        {
            return abbruch;
        }

        var beleg = await db.BelegSperrenAsync(command.FreizeitId, command.BelegId, cancellationToken);
        if (beleg is null)
        {
            return new BelegAenderung.NichtGefunden();
        }

        var titel = string.IsNullOrWhiteSpace(command.Dateiname) ? StandardTitel : DokumentDateien.TitelAusDateiname(command.Dateiname);
        await speicher.SpeichernAsync(Beleg.DateiPraefix, beleg.Id, command.Inhalt, DateiPruefung.ContentType(command.Art), cancellationToken);
        beleg.DateiSetzen(command.Art, DokumentDateien.Dateiname(titel, command.Art));
        return new BelegAenderung.Gespeichert(beleg);
    }
}

/// <summary>Story 5-2: Entfernt die Datei eines Belegs; der Beleg bleibt. Ohne Datei ändert sich nichts. Eine abgeschlossene Freizeit ist nur lesbar.</summary>
internal sealed record BelegDateiEntfernenCommand(Guid FreizeitId, Guid BelegId)
    : ICommand<BelegAenderung>, IModulCommand<FinanzenDbContext>, IBenutzerCommand, ISchalterCommand
{
    public FunktionsSchalter Schalter => FunktionsSchalter.Finanzen;
}

internal sealed class BelegDateiEntfernenHandler(FinanzenDbContext db, IFreizeitZeitpunkte zeitpunkte, IDateiSpeicher speicher)
    : ICommandHandler<BelegDateiEntfernenCommand, BelegAenderung>
{
    public async ValueTask<BelegAenderung> Handle(BelegDateiEntfernenCommand command, CancellationToken cancellationToken)
    {
        if (await BelegSchritte.FreizeitPruefenAsync(zeitpunkte, command.FreizeitId, cancellationToken) is { } abbruch)
        {
            return abbruch;
        }

        var beleg = await db.BelegSperrenAsync(command.FreizeitId, command.BelegId, cancellationToken);
        if (beleg is null)
        {
            return new BelegAenderung.NichtGefunden();
        }

        if (beleg.DateiArt is not null)
        {
            await speicher.LoeschenAsync(Beleg.DateiPraefix, beleg.Id, cancellationToken);
            beleg.DateiEntfernen();
        }

        return new BelegAenderung.Geloescht();
    }
}

/// <summary>
/// Endpunkte der Datei eines Belegs unter <c>/api/freizeiten/{freizeitId}/finanzen/belege/{belegId}/datei</c> (Leitung und Team, Modul Finanzen, AD-15):
/// <list type="bullet">
/// <item><c>POST</c> Multipart im Feld <c>datei</c> (JPEG, PNG, WebP oder PDF bis 10 MB, geprüft am Inhalt) → <c>200</c> mit dem Beleg; sonst <c>400</c> am
/// Feld <c>datei</c> mit den Texten der Dokumente.</item>
/// <item><c>GET</c> → die Datei: Bilder <c>inline</c>, PDF als Anhang, mit <c>nosniff</c>; ohne Datei <c>404</c>.</item>
/// <item><c>DELETE</c> → <c>204</c>, der Beleg bleibt.</item>
/// </list>
/// Nach dem Abschluss ergeben Hochladen und Entfernen <c>409 freizeit-abgeschlossen</c>. Beleg unbekannt oder aus einer anderen Freizeit <c>404</c>.
/// </summary>
internal static class BelegDateiEndpunkte
{
    public static async Task<IResult> Hochladen(Guid freizeitId, Guid belegId, HttpRequest anfrage, ISender sender, CancellationToken abbruch)
    {
        var formular = anfrage.HasFormContentType ? await anfrage.ReadFormAsync(abbruch) : null;
        var (datei, fehler) = await DokumentDateien.LesenAsync(formular, BelegFelder.Datei, abbruch);
        if (fehler is not null)
        {
            throw new ValidierungsAusnahme(new Dictionary<string, string[]> { [BelegFelder.Datei] = [fehler] });
        }

        var ergebnis = await sender.Send(new BelegDateiHochladenCommand(freizeitId, belegId, datei!.Inhalt, datei.Art, datei.Dateiname), abbruch);
        return BelegSchritte.Antwort(ergebnis, beleg => TypedResults.Ok(BelegSchritte.AlsApi(beleg)));
    }

    public static async Task<IResult> Ausliefern(Guid freizeitId, Guid belegId, FinanzenLeseDbContext db, IDateiSpeicher speicher, CancellationToken abbruch)
    {
        var beleg = await db.Belege.SingleOrDefaultAsync(b => b.Id == belegId && b.FreizeitId == freizeitId, abbruch);
        if (beleg?.DateiArt is not { } art)
        {
            return TypedResults.NotFound();
        }

        var datei = await speicher.LesenAsync(Beleg.DateiPraefix, beleg.Id, abbruch);
        return datei is not null && DateiPruefung.ArtErkennen(datei.Inhalt) is not null
            ? DateiAuslieferung.Ausliefern(datei.Inhalt, beleg.Dateiname ?? DokumentDateien.Dateiname("Beleg", art))
            : TypedResults.NotFound();
    }

    public static async Task<IResult> Entfernen(Guid freizeitId, Guid belegId, ISender sender, CancellationToken abbruch) =>
        BelegSchritte.Antwort(await sender.Send(new BelegDateiEntfernenCommand(freizeitId, belegId), abbruch), _ => TypedResults.NoContent());
}
