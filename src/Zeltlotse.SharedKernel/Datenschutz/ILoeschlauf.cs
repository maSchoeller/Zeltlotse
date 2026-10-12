namespace Zeltlotse.SharedKernel.Datenschutz;

/// <summary>
/// Täglicher Lösch-Lauf eines Moduls mit Fristen (AD-16, NFR-5). Der Lösch-Dienst des API-Hosts ruft jede Umsetzung in eigenem
/// DI-Scope auf. Eine Umsetzung nutzt nur <c>ISender</c> und schickt einen internen Command; geschrieben wird also weiter nur in
/// Command-Handlern (AD-27).
/// </summary>
public interface ILoeschlauf
{
    Task LaufAsync(LoeschAuftrag auftrag, CancellationToken abbruch);
}

/// <summary>
/// Was heute fällig ist: die Uhrzeit des Laufs, die Freizeiten, deren Daten nach der Frist der Organisation abgelaufen sind
/// (Freizeitende + Monate ≤ jetzt), und je Organisation die Frist für abgelaufene Nachweis-Bestätigungen in Monaten.
/// </summary>
public sealed record LoeschAuftrag(
    DateTimeOffset Jetzt,
    IReadOnlyList<Guid> FreizeitenMitAbgelaufenenDaten,
    IReadOnlyList<NachweisBestaetigungFrist> NachweisBestaetigungFristen);

/// <summary>Frist einer Organisation für abgelaufene Nachweis-Bestätigungen: Monate nach „gültig bis“.</summary>
public sealed record NachweisBestaetigungFrist(Guid OrganisationId, int Monate);

/// <summary>
/// Feste technische Fristen für Zugangslinks ohne fachlichen Wert (keine Org-Richtlinie): Login-Anfragen 1 Tag, Einladungen
/// und Übergaben 30 Tage nach ihrem Ablauf.
/// </summary>
public static class TechnischeFristen
{
    public static readonly TimeSpan LoginAnfragen = TimeSpan.FromDays(1);

    public static readonly TimeSpan Einladungen = TimeSpan.FromDays(30);
}
