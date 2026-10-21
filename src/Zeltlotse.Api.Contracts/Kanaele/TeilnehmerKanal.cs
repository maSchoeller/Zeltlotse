namespace Zeltlotse.Api.Contracts.Kanaele;

/// <summary>
/// Story 6-4 (FR-61, FR-64): Antwort von <c>GET /api/teilnehmer/kanal</c> (Gast-Sitzung): höchstens die neuesten 200 Nachrichten im Kanal „Alle“, älteste
/// zuerst. Ohne Autor; den Kanal „Team“ gibt es für Gäste nicht.
/// </summary>
public sealed record TeilnehmerKanal(IReadOnlyList<TeilnehmerNachricht> Nachrichten);

/// <summary>Story 6-4: Eine Nachricht im Kanal „Alle“ für Teilnehmende, ohne <c>autorPersonId</c>.</summary>
public sealed record TeilnehmerNachricht(Guid Id, string Text, DateTimeOffset GesendetAm);
