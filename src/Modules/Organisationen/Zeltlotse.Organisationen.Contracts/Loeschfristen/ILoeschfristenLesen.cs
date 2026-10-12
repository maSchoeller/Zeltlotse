namespace Zeltlotse.Organisationen.Contracts.Loeschfristen;

/// <summary>
/// Lesende Schnittstelle von Organisationen für den Lösch-Dienst des API-Hosts (AD-16, NFR-5): die Löschfristen aller
/// Organisationen. Liest ohne Mandantenfilter; nur der Lösch-Dienst ruft sie auf (Ausnahme laut AD-6). Fehlt die Zeile einer
/// Organisation, gelten die Vorgabewerte.
/// </summary>
public interface ILoeschfristenLesen
{
    Task<IReadOnlyList<OrgLoeschfristen>> AlleAsync(CancellationToken abbruch = default);
}

/// <summary>Löschfristen einer Organisation in Monaten: Daten einer Freizeit ab Freizeitende, Nachweis-Bestätigungen ab „gültig bis“.</summary>
public sealed record OrgLoeschfristen(Guid OrganisationId, int FreizeitDatenMonate, int NachweisBestaetigungMonate);
