using System.Xml.Linq;

namespace Zeltlotse.ArchitectureTests;

/// <summary>
/// Regeln, die sich nur an den Projektdateien prüfen lassen: Projekt- und Paketreferenzen
/// erscheinen nicht vollständig in den gebauten Assemblies (ein Quellgenerator gar nicht).
/// Jede Regel liefert die Verstöße als lesbare Sätze; leer heißt: eingehalten.
/// </summary>
internal static class ProjektDateiRegeln
{
    private static readonly HashSet<string> ErlaubteFremdeProjekte =
        ["Zeltlotse.SharedKernel", "Zeltlotse.Api.Contracts"];

    /// <summary>AD-2/AD-24: Ein Modul referenziert fremde Module nur über deren Contracts.</summary>
    public static IEnumerable<string> ModulReferenziertNurContracts(string projekt, XDocument projektDatei)
    {
        foreach (var referenz in ProjektReferenzen(projektDatei))
        {
            if (ErlaubteFremdeProjekte.Contains(referenz) || referenz == projekt + ".Contracts")
            {
                continue;
            }

            var istModulContracts = Loesung.Module.Any(m => m.ContractsProjekt == referenz);
            if (!istModulContracts)
            {
                yield return $"{projekt} referenziert {referenz}; erlaubt sind nur SharedKernel, Api.Contracts und Modul-Contracts.";
            }
        }
    }

    /// <summary>AD-24: Ein Contracts-Projekt referenziert höchstens den SharedKernel.</summary>
    public static IEnumerable<string> ContractsReferenzierenKeinModul(string projekt, XDocument projektDatei) =>
        ProjektReferenzen(projektDatei)
            .Where(r => r != "Zeltlotse.SharedKernel")
            .Select(r => $"{projekt} referenziert {r}; ein Contracts-Projekt darf nur den SharedKernel referenzieren.");

    /// <summary>AD-27: Nur der API-Host (und Testprojekte) bindet <c>Mediator.SourceGenerator</c> ein.</summary>
    public static IEnumerable<string> KeinMediatorGenerator(string projekt, XDocument projektDatei) =>
        PaketReferenzen(projektDatei)
            .Where(p => string.Equals(p, "Mediator.SourceGenerator", StringComparison.OrdinalIgnoreCase))
            .Select(_ => $"{projekt} referenziert Mediator.SourceGenerator; der Generator gehört nur in den API-Host.");

    /// <summary>AD-5: <c>Zeltlotse.Api.Contracts</c> hat weder Projekt- noch Paketreferenzen.</summary>
    public static IEnumerable<string> ApiContractsOhneReferenzen(string projekt, XDocument projektDatei) =>
        ProjektReferenzen(projektDatei).Concat(PaketReferenzen(projektDatei))
            .Select(r => $"{projekt} referenziert {r}; Api.Contracts darf nichts außer der .NET-Basisbibliothek nutzen.");

    public static IEnumerable<string> ProjektReferenzen(XDocument projektDatei) =>
        projektDatei.Descendants().Where(e => e.Name.LocalName == "ProjectReference")
            .Select(e => (string?)e.Attribute("Include"))
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Select(i => Path.GetFileNameWithoutExtension(i!.Replace('\\', '/')));

    public static IEnumerable<string> PaketReferenzen(XDocument projektDatei) =>
        projektDatei.Descendants().Where(e => e.Name.LocalName == "PackageReference")
            .Select(e => (string?)e.Attribute("Include") ?? (string?)e.Attribute("Update"))
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Cast<string>();
}
