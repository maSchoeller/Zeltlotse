using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Kanaele;
using Zeltlotse.Freizeiten.Contracts.Namen;
using Zeltlotse.Freizeiten.Contracts.Schalter;
using Zeltlotse.Freizeiten.Contracts.TeilnehmerZugang;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Kanaele.Daten;
using Zeltlotse.Kanaele.Push;
using Zeltlotse.SharedKernel.Push;
using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Kanaele.Nachrichten;

/// <summary>
/// Story 6-4 (AD-13, AD-21): Push-Auftrag „neue Nachricht in Alle“ an die QR-Gäste, nur die Kennung der Nachricht. Er entsteht neben
/// <see cref="NachrichtPush"/> mit jeder bestätigten Nachricht im Kanal „Alle“; Inhalt und Empfänger entstehen erst beim Versand
/// (<see cref="NachrichtGastPushAufbau"/>).
/// </summary>
internal sealed record NachrichtGastPush(Guid NachrichtId) : IPushAuftrag;

/// <summary>
/// Baut den Gast-Push erst beim Versand, unter dem Mandanten aus dem Umschlag: Empfänger sind die Gast-Abos der Freizeit mit dem aktuellen Stempel des
/// QR-Links (nach „Link erneuern“ keine alten) und ohne Geräte, die schon ein Team-Abo der Freizeit haben, aber nur, solange der
/// Teilnehmer-Zugang an ist und das Freizeitende nicht erreicht ist (Pfeil Kanäle → Freizeiten). Gleicher Inhalt wie beim Team (Freizeit, „Alle“, gekürzter
/// Text), die Adresse ist der Teilnehmer-Zugang <c>/app/teilnehmer</c>. Abgelaufene Gast-Abos räumt ein eigener Command ab.
/// </summary>
internal sealed class NachrichtGastPushAufbau(
    KanaeleLeseDbContext db, IFreizeitSchalterLesen schalter, IFreizeitZeitpunkte zeitpunkte, IQrZugangLesen qrZugang, IFreizeitNamenLesen namen, TimeProvider uhr,
    ISender sender)
    : IPushAufbau<NachrichtGastPush>
{
    /// <summary>Die Seite des Teilnehmer-Zugangs in der Web-App.</summary>
    public const string Url = "/app/teilnehmer";

    public async Task<PushSendung?> AufbauenAsync(NachrichtGastPush auftrag, CancellationToken abbruch)
    {
        var nachricht = await db.Nachrichten.Where(n => n.Id == auftrag.NachrichtId && n.Kanal == Kanal.Alle)
            .Select(n => new { n.FreizeitId, n.Text })
            .SingleOrDefaultAsync(abbruch);
        if (nachricht is null
            || !(await schalter.SchalterAsync(nachricht.FreizeitId, abbruch)).Contains(FunktionsSchalter.TeilnehmerZugang)
            || !(await zeitpunkte.ZeitpunkteAsync([nachricht.FreizeitId], abbruch)).TryGetValue(nachricht.FreizeitId, out var zeiten)
            || uhr.GetUtcNow() >= zeiten.Freizeitende
            || await qrZugang.StempelAsync(nachricht.FreizeitId, abbruch) is not { } stempel)
        {
            return null;
        }

        var ziele = await db.GastPushAbos
            .Where(a => a.FreizeitId == nachricht.FreizeitId && a.Stempel == stempel)
            // Hat dasselbe Gerät auch ein Team-Abo der Freizeit, kommt die Nachricht schon darüber; kein zweiter Push.
            .Where(a => !db.PushAbos.Any(t => t.FreizeitId == a.FreizeitId && t.Endpunkt == a.Endpunkt))
            .OrderBy(a => a.Id)
            .Select(a => new PushZiel(a.Id, a.Endpunkt, a.P256dh, a.Auth))
            .ToListAsync(abbruch);
        if (ziele.Count == 0)
        {
            return null;
        }

        var freizeit = (await namen.NamenAsync([nachricht.FreizeitId], abbruch)).GetValueOrDefault(nachricht.FreizeitId) ?? "Zeltlotse";
        return new PushSendung(new PushInhalt(NachrichtPushText.Titel(freizeit, Kanal.Alle), NachrichtPushText.Kuerzen(nachricht.Text), Url), ziele);
    }

    /// <summary>Abgelaufene Gast-Abos löscht ein eigener Command (geschrieben wird nur in Command-Handlern, AD-27).</summary>
    public async Task UngueltigeZieleAsync(IReadOnlyCollection<Guid> zielIds, CancellationToken abbruch) =>
        await sender.Send(new GastPushAbosEntfernen([.. zielIds]), abbruch);
}
