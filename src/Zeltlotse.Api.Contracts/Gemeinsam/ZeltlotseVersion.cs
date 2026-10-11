using System.Reflection;

namespace Zeltlotse.Api.Contracts.Gemeinsam;

/// <summary>
/// Version des Standes (AD-5): die <c>AssemblyInformationalVersion</c> dieser Assembly. Server und Web-App lesen
/// dieselbe Assembly; jede Antwort trägt den Wert in <see cref="Kopfzeilen.Version"/>, und die App bietet bei
/// Abweichung „Neu laden“ an. Weil SourceLink den Commit anhängt, unterscheidet sich jeder neue Stand.
/// </summary>
public static class ZeltlotseVersion
{
    public static string Aktuell { get; } =
        typeof(ZeltlotseVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(ZeltlotseVersion).Assembly.GetName().Version?.ToString()
        ?? "0";
}
