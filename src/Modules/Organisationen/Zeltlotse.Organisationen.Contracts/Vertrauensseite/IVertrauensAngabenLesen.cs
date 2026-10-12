using Zeltlotse.Organisationen.Contracts.Nachweise;

namespace Zeltlotse.Organisationen.Contracts.Vertrauensseite;

/// <summary>
/// Lesende Schnittstelle von Organisationen (AD-2, FR-66): die gespeicherten Einstellungen der Organisation, aus denen die
/// Vertrauensseite einer Freizeit entsteht. Liest nur die Organisation des Mandanten-Kontexts (bei einem Anmeldelink: die
/// Organisation der Freizeit des Tokens, AD-6); ohne Kontext oder für eine unbekannte Organisation <c>null</c>.
/// </summary>
public interface IVertrauensAngabenLesen
{
    Task<VertrauensAngaben?> LesenAsync(CancellationToken abbruch = default);
}

/// <summary>
/// Name, Löschfristen in Monaten (Daten einer Freizeit ab Freizeitende, abgelaufene Nachweis-Bestätigungen) und die
/// Pflicht-Nachweise in Enum-Reihenfolge.
/// </summary>
public sealed record VertrauensAngaben(string Name, int FreizeitDatenMonate, int NachweisBestaetigungMonate, IReadOnlyList<NachweisArt> PflichtArten);
