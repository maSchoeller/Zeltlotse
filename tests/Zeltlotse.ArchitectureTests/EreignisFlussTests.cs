using System.Text.RegularExpressions;
using Zeltlotse.SharedKernel.Ereignisse;

namespace Zeltlotse.ArchitectureTests;

/// <summary>
/// AD-4/AD-24: Jedes Paar (Ereignis, Zielmodul) im Code steht in der Tabelle *Ereignis-Fluss* der Spine,
/// und jeder Ereignis-Typ der Modul-Contracts steht dort. Tabellenzeilen ohne Code sind erlaubt, bis ihre Story kommt.
/// </summary>
public sealed partial class EreignisFlussTests
{
    private const string JedesModul = "*";

    [Fact]
    public void Tabelle_wird_aus_der_Spine_gelesen()
    {
        var tabelle = EreignisFluss();

        Assert.Equal(10, tabelle.Count);
        Assert.Equal("Freizeiten", tabelle["AnmeldungBestaetigt"].Quelle);
        Assert.Equal(["Organisationen", "Personen"], tabelle["AnmeldungBestaetigt"].Ziele.Order());
        Assert.Equal([JedesModul], tabelle["PersonGeloescht"].Ziele);
        Assert.Equal(["Kanaele", "Sicherheit"], tabelle["FreizeitAbgeschlossen"].Ziele.Order());
    }

    [Fact]
    public void Jeder_Empfaenger_im_Code_steht_in_der_Tabelle()
    {
        var tabelle = EreignisFluss();
        var verstoesse = new List<string>();

        foreach (var modul in Loesung.Module)
        {
            foreach (var typ in Loesung.AssemblyVon(modul.Projekt)!.GetTypes())
            {
                foreach (var ereignis in EmpfangeneEreignisse(typ))
                {
                    var quelle = Loesung.Module.SingleOrDefault(m => m.EventsNamensraum == ereignis.Namespace);
                    var gefunden = tabelle.TryGetValue(ereignis.Name, out var zeile)
                                   && quelle is not null
                                   && zeile.Quelle == quelle.Name
                                   && (zeile.Ziele.Contains(modul.Name) || zeile.Ziele.Contains(JedesModul));
                    if (!gefunden)
                    {
                        verstoesse.Add($"{typ.FullName} empfängt {ereignis.FullName}, das Paar fehlt in der Tabelle Ereignis-Fluss.");
                    }
                }
            }
        }

        Assert.Empty(verstoesse);
    }

    [Fact]
    public void Jeder_Ereignis_Typ_der_Contracts_steht_in_der_Tabelle()
    {
        var tabelle = EreignisFluss();
        var verstoesse = new List<string>();

        foreach (var modul in Loesung.Module)
        {
            var typen = Loesung.AssemblyVon(modul.ContractsProjekt)!.GetTypes()
                .Where(t => t.IsPublic && t.Namespace == modul.EventsNamensraum && !t.IsNested && !t.IsEnum && !t.Name.StartsWith('<'))
                .ToList();
            var unterTypen = typen
                .SelectMany(t => t.GetProperties().Select(p => Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType))
                .ToHashSet();

            foreach (var typ in typen.Where(t => !unterTypen.Contains(t)))
            {
                if (!tabelle.TryGetValue(typ.Name, out var zeile) || zeile.Quelle != modul.Name)
                {
                    verstoesse.Add($"{typ.FullName} steht nicht in der Tabelle Ereignis-Fluss.");
                }
            }
        }

        Assert.Empty(verstoesse);
    }

    [Fact]
    public void Empfaenger_Typ_liefert_sein_Ereignis()
    {
        var testmodul = Loesung.AssemblyVon("Zeltlotse.Testmodul")!;
        var empfaenger = testmodul.GetType("Zeltlotse.Testmodul.Empfang.AngelegtEmpfangenA", throwOnError: true)!;
        var keinEmpfaenger = testmodul.GetType("Zeltlotse.Testmodul.Notizen.NotizAnlegen", throwOnError: true)!;

        Assert.Equal([testmodul.GetType("Zeltlotse.Testmodul.Events.NotizAngelegt")], EmpfangeneEreignisse(empfaenger));
        Assert.Empty(EmpfangeneEreignisse(keinEmpfaenger));
    }

    internal static IEnumerable<Type> EmpfangeneEreignisse(Type typ) =>
        typ.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEreignisEmpfaenger<,>))
            .Select(i => i.GetGenericArguments()[1]);

    internal static Dictionary<string, (string Quelle, HashSet<string> Ziele)> EreignisFluss()
    {
        var zeilen = File.ReadAllLines(Loesung.SpinePfad);
        var start = Array.FindIndex(zeilen, z => z.StartsWith("### Ereignis-Fluss", StringComparison.Ordinal));
        Assert.True(start >= 0, "Abschnitt Ereignis-Fluss fehlt in der Spine.");

        var ergebnis = new Dictionary<string, (string, HashSet<string>)>();
        foreach (var zeile in zeilen.Skip(start + 1).SkipWhile(z => !z.StartsWith('|')).TakeWhile(z => z.StartsWith('|')))
        {
            var treffer = ZeileMuster().Match(zeile);
            if (!treffer.Success)
            {
                continue;
            }

            var ziele = treffer.Groups["ziele"].Value
                .Split(';')
                .Select(teil => teil.Split('→')[0].Trim())
                .Select(ziel => ziel.StartsWith("jedes Modul", StringComparison.Ordinal) ? JedesModul : ModulNachAnzeigename(ziel))
                .ToHashSet();
            ergebnis[treffer.Groups["ereignis"].Value] = (ModulNachAnzeigename(treffer.Groups["quelle"].Value), ziele);
        }

        return ergebnis;
    }

    private static string ModulNachAnzeigename(string anzeigename) =>
        Loesung.Module.SingleOrDefault(m => m.Anzeigename == anzeigename)?.Name
        ?? throw new InvalidOperationException($"Unbekanntes Modul in der Tabelle Ereignis-Fluss: {anzeigename}");

    [GeneratedRegex(@"^\|\s*`(?<ereignis>\w+)`\s*\((?<quelle>[^)]+)\)\s*\|\s*(?<ziele>.+?)\s*\|\s*$")]
    private static partial Regex ZeileMuster();
}
