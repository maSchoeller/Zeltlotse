using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ArchUnitNET.Domain;
using ArchUnitNET.Domain.Extensions;
using ArchUnitNET.Fluent;
using Microsoft.AspNetCore.Components;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Zeltlotse.ArchitectureTests;

/// <summary>
/// Regeln für die Clients (AD-5, AD-24, AD-29): Web-App, öffentliche Seiten, App-Bausteine und Komponenten-Bibliothek.
/// Jede Regel liefert die Verstöße als lesbare Sätze bzw. als ArchUnit-Regel; leer heißt: eingehalten.
/// </summary>
internal static partial class ClientRegeln
{
    public const string Komponenten = "Zeltlotse.Ui.Komponenten";
    public const string Ui = "Zeltlotse.Ui";
    public const string App = "Zeltlotse.App";
    public const string Public = "Zeltlotse.Public";

    /// <summary>Die vier Client-Projekte aus dem Quellbaum der Spine.</summary>
    public static readonly IReadOnlyList<string> Clients = [Komponenten, Ui, App, Public];

    private static readonly string[] ErlaubteKomponentenPakete =
        ["Microsoft.AspNetCore.Components", "Microsoft.AspNetCore.Components.Web"];

    private const string NurInteraktiv = "Zeltlotse.Ui.Komponenten.NurInteraktivAttribute";
    private const string BrowserSchnittstelle = "Zeltlotse.App.Browser.IBrowser";

    /// <summary>AD-5/AD-24: Clients referenzieren weder Modul-Projekte noch Modul-Contracts noch den SharedKernel.</summary>
    public static IEnumerable<string> ClientReferenziertKeinModul(string projekt, XDocument projektDatei)
    {
        foreach (var referenz in ProjektDateiRegeln.ProjektReferenzen(projektDatei))
        {
            var verboten = referenz == "Zeltlotse.SharedKernel"
                           || Loesung.Module.Any(m => m.Projekt == referenz || m.ContractsProjekt == referenz);
            if (verboten)
            {
                yield return $"{projekt} referenziert {referenz}; Clients nutzen nur Ui.Komponenten, Ui und Api.Contracts.";
            }
        }
    }

    /// <summary>AD-29: Ui.Komponenten hat keine Projektreferenz und nur die zwei erlaubten Pakete.</summary>
    public static IEnumerable<string> KomponentenOhneFremdeReferenzen(XDocument projektDatei) =>
        ProjektDateiRegeln.ProjektReferenzen(projektDatei)
            .Select(r => $"{Komponenten} referenziert das Projekt {r}; erlaubt ist keines.")
            .Concat(ProjektDateiRegeln.PaketReferenzen(projektDatei)
                .Where(p => !ErlaubteKomponentenPakete.Contains(p, StringComparer.OrdinalIgnoreCase))
                .Select(p => $"{Komponenten} referenziert das Paket {p}; erlaubt sind nur {string.Join(" und ", ErlaubteKomponentenPakete)}."));

    /// <summary>AD-29: Clients nutzen keine Pakete außerhalb von <c>Microsoft.*</c> (keine fremden Komponenten-Bibliotheken).</summary>
    public static IEnumerable<string> ClientPaketeNurMicrosoft(string projekt, XDocument projektDatei) =>
        ProjektDateiRegeln.PaketReferenzen(projektDatei)
            .Where(p => !p.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase))
            .Select(p => $"{projekt} referenziert das Paket {p}; Clients nutzen nur Pakete von Microsoft.");

    /// <summary>AD-24/AD-29: Die öffentlichen Seiten verwenden keinen <c>[NurInteraktiv]</c>-Baustein.</summary>
    public static IArchRule OhneNurInteraktiv(params string[] assemblies) =>
        Types().That().FollowCustomPredicate(t => AbhaengigkeitsRegeln.InAssembly(t, assemblies), "Typen der öffentlichen Seiten")
            .Should().FollowCustomCondition(
                t => !t.GetTypeDependencies().Any(IstNurInteraktiv),
                "keinen [NurInteraktiv]-Baustein verwenden",
                "verwendet einen [NurInteraktiv]-Baustein");

    /// <summary>AD-29: Nur die Umsetzung von <c>IBrowser</c> (samt geschachtelter Typen) hängt von <c>Microsoft.JSInterop</c> ab.</summary>
    public static IArchRule NurBrowserNutztJsInterop(Architecture architektur, params string[] assemblies) =>
        Types().That().FollowCustomPredicate(t => AbhaengigkeitsRegeln.InAssembly(t, assemblies), "Typen der Clients")
            .Should().FollowCustomCondition(
                t => !t.GetTypeDependencies().Any(IstJsInterop) || SetztBrowserUm(architektur, t),
                "nur als Umsetzung von IBrowser von Microsoft.JSInterop abhängen",
                "hängt von Microsoft.JSInterop ab, setzt aber IBrowser nicht um");

    /// <summary>AD-29: Ein Baustein mit einem Parameter vom Typ <c>EventCallback</c> trägt <c>[NurInteraktiv]</c>.</summary>
    public static IEnumerable<string> EventCallbackNurMitNurInteraktiv(IEnumerable<Type> typen) =>
        typen
            .Where(t => typeof(IComponent).IsAssignableFrom(t) && !t.IsAbstract)
            .Where(t => !t.GetCustomAttributes(false).Any(a => a.GetType().FullName == NurInteraktiv))
            .SelectMany(t => t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(p => p.IsDefined(typeof(ParameterAttribute), true) && IstEventCallback(p.PropertyType))
                .Select(p => $"{t.FullName}.{p.Name} ist ein EventCallback-Parameter, aber {t.Name} trägt kein [NurInteraktiv]."));

    /// <summary>
    /// CSP und Komponenten-Bibliothek (AD-25, AD-29) im Quelltext von Razor- und HTML-Dateien: kein <c>style=</c>, kein
    /// <c>&lt;style</c>, kein <c>&lt;script&gt;</c> ohne <c>src</c>; Klassen mit <c>elkw-</c>/<c>zl-</c> nur in Ui.Komponenten.
    /// </summary>
    public static IEnumerable<string> QuelltextVerstoesse(string projekt, string datei, string inhalt)
    {
        if (StilAttribut().IsMatch(inhalt))
        {
            yield return $"{datei}: style-Attribut (CSP).";
        }

        if (StilBlock().IsMatch(inhalt))
        {
            yield return $"{datei}: style-Block (CSP).";
        }

        if (InlineSkript().IsMatch(inhalt))
        {
            yield return $"{datei}: script ohne src (CSP).";
        }

        if (projekt != Komponenten)
        {
            foreach (Match klasse in KlassenAttribut().Matches(inhalt))
            {
                var bibliotheksKlasse = klasse.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault(k => k.StartsWith("elkw-", StringComparison.Ordinal) || k.StartsWith("zl-", StringComparison.Ordinal));
                if (bibliotheksKlasse is not null)
                {
                    yield return $"{datei}: Klasse {bibliotheksKlasse} außerhalb von {Komponenten}.";
                }
            }
        }
    }

    private static bool IstEventCallback(Type typ) =>
        typ == typeof(EventCallback) || (typ.IsGenericType && typ.GetGenericTypeDefinition() == typeof(EventCallback<>));

    private static bool IstNurInteraktiv(IType typ) =>
        typ.Attributes.Any(a => a.FullName == NurInteraktiv);

    private static bool IstJsInterop(IType typ) =>
        typ.Namespace?.FullName is { } ns && (ns == "Microsoft.JSInterop" || ns.StartsWith("Microsoft.JSInterop.", StringComparison.Ordinal));

    private static bool SetztBrowserUm(Architecture architektur, IType typ)
    {
        var trenner = typ.FullName.IndexOfAny(['+', '/']);
        var aeusserer = trenner < 0 ? typ : architektur.Types.FirstOrDefault(t => t.FullName == typ.FullName[..trenner]) ?? typ;
        return aeusserer.ImplementedInterfaces.Any(i => i.FullName == BrowserSchnittstelle);
    }

    [GeneratedRegex(@"\sstyle\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex StilAttribut();

    [GeneratedRegex(@"<style[\s>]", RegexOptions.IgnoreCase)]
    private static partial Regex StilBlock();

    [GeneratedRegex(@"<script(?![^>]*\bsrc\s*=)[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex InlineSkript();

    [GeneratedRegex(@"\bclass\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex KlassenAttribut();
}
