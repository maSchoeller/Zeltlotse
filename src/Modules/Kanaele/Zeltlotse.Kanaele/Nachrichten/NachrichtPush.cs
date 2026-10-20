using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Kanaele;
using Zeltlotse.Freizeiten.Contracts.Namen;
using Zeltlotse.Freizeiten.Contracts.Team;
using Zeltlotse.Kanaele.Daten;
using Zeltlotse.Kanaele.Push;
using Zeltlotse.SharedKernel.Push;

namespace Zeltlotse.Kanaele.Nachrichten;

/// <summary>
/// Story 6-1 (AD-13, AD-21): Push-Auftrag „neue Nachricht“, nur die Kennung der Nachricht. Freizeitname, Kanal, Text und Empfänger entstehen erst beim
/// Versand (<see cref="NachrichtPushAufbau"/>); nichts davon steht in der Outbox. Der Auftrag entsteht nur mit dem bestätigten Senden einer Nachricht.
/// </summary>
internal sealed record NachrichtPush(Guid NachrichtId) : IPushAuftrag;

/// <summary>Story 6-1: Titel, Text und Ziel-Adresse des Push.</summary>
internal static class NachrichtPushText
{
    /// <summary>So viele Zeichen der Nachricht stehen höchstens im Push.</summary>
    public const int TextLaenge = 150;

    public static string KanalText(Kanal kanal) => kanal == Kanal.Alle ? "Alle" : "Team";

    /// <summary>„Zeltlager Kapf 2027 · Team“.</summary>
    public static string Titel(string freizeit, Kanal kanal) => $"{freizeit} · {KanalText(kanal)}";

    /// <summary>Der Text, auf <see cref="TextLaenge"/> Zeichen gekürzt (mit „…“ am Ende); ein Emoji (Ersatzzeichen-Paar) wird nie zerteilt.</summary>
    public static string Kuerzen(string text)
    {
        if (text.Length <= TextLaenge)
        {
            return text;
        }

        var laenge = TextLaenge - 1;
        if (char.IsHighSurrogate(text[laenge - 1]))
        {
            laenge--;
        }

        return string.Concat(text.AsSpan(0, laenge).TrimEnd(), "…");
    }

    /// <summary>Die Kanal-Seite der Web-App; „Alle“ mit <c>?kanal=alle</c>.</summary>
    public static string Url(Guid freizeitId, Kanal kanal) => $"/app/freizeiten/{freizeitId}/kanaele" + (kanal == Kanal.Alle ? "?kanal=alle" : "");
}

/// <summary>
/// Baut den Push erst beim Versand, unter dem Mandanten aus dem Umschlag: Nachricht und Abos aus Kanäle, Team und Freizeitname aus Freizeiten (Pfeil
/// Kanäle → Freizeiten). Empfänger sind die Abos der Freizeit, deren Person gerade Leitung oder Team ist, ohne die Person, die geschrieben hat. Der Inhalt
/// nennt Freizeit, Kanal und den gekürzten Text, keinen Autor-Namen und keine Gesundheitsdaten. Ohne Empfänger oder ohne Nachricht gibt es keinen Push.
/// </summary>
internal sealed class NachrichtPushAufbau(KanaeleLeseDbContext db, IFreizeitTeamLesen team, IFreizeitNamenLesen namen, ISender sender)
    : IPushAufbau<NachrichtPush>
{
    public async Task<PushSendung?> AufbauenAsync(NachrichtPush auftrag, CancellationToken abbruch)
    {
        var nachricht = await db.Nachrichten.Where(n => n.Id == auftrag.NachrichtId)
            .Select(n => new { n.FreizeitId, n.Kanal, n.AutorPersonId, n.Text })
            .SingleOrDefaultAsync(abbruch);
        if (nachricht is null)
        {
            return null;
        }

        var empfaenger = (await team.TeamAsync(nachricht.FreizeitId, abbruch)).Select(m => m.PersonId).Where(p => p != nachricht.AutorPersonId).ToList();
        var ziele = await db.PushAbos
            .Where(a => a.FreizeitId == nachricht.FreizeitId && empfaenger.Contains(a.PersonId))
            .OrderBy(a => a.Id)
            .Select(a => new PushZiel(a.Id, a.Endpunkt, a.P256dh, a.Auth))
            .ToListAsync(abbruch);
        if (ziele.Count == 0)
        {
            return null;
        }

        var freizeit = (await namen.NamenAsync([nachricht.FreizeitId], abbruch)).GetValueOrDefault(nachricht.FreizeitId) ?? "Zeltlotse";
        return new PushSendung(
            new PushInhalt(
                NachrichtPushText.Titel(freizeit, nachricht.Kanal),
                NachrichtPushText.Kuerzen(nachricht.Text),
                NachrichtPushText.Url(nachricht.FreizeitId, nachricht.Kanal)),
            ziele);
    }

    /// <summary>Abgelaufene Abos löscht ein eigener Command (geschrieben wird nur in Command-Handlern, AD-27).</summary>
    public async Task UngueltigeZieleAsync(IReadOnlyCollection<Guid> zielIds, CancellationToken abbruch) =>
        await sender.Send(new PushAbosEntfernen([.. zielIds]), abbruch);
}
