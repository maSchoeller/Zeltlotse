using System.Xml.Linq;
using ArchUnitNET.xUnitV3;

namespace Zeltlotse.ArchitectureTests;

/// <summary>Regeln für die Clients nach AD-5, AD-24, AD-25 und AD-29, jeweils mit Negativ-Fixture.</summary>
public sealed class ClientTests
{
    public static TheoryData<string> Clients => [.. ClientRegeln.Clients];

    [Fact]
    public void Alle_vier_Clients_sind_gebaut_und_geladen()
    {
        foreach (var client in ClientRegeln.Clients)
        {
            Assert.NotNull(Loesung.AssemblyVon(client));
        }
    }

    [Theory]
    [MemberData(nameof(Clients))]
    public void Clients_referenzieren_weder_Module_noch_Modul_Contracts_noch_SharedKernel(string client)
    {
        Assert.Empty(ClientRegeln.ClientReferenziertKeinModul(client, Projekt(client)));
    }

    [Theory]
    [MemberData(nameof(Clients))]
    public void Clients_haengen_auch_gebaut_nicht_von_Modulen_oder_SharedKernel_ab(string client)
    {
        var referenzen = Loesung.AssemblyVon(client)!.GetReferencedAssemblies().Select(a => a.Name!).ToList();

        Assert.DoesNotContain("Zeltlotse.SharedKernel", referenzen);
        Assert.DoesNotContain(referenzen, r => Loesung.Module.Any(m => m.Projekt == r || m.ContractsProjekt == r));
    }

    [Fact]
    public void Negativ_Client_mit_Modul_Contracts_und_SharedKernel_wird_erkannt()
    {
        var verstoesse = ClientRegeln.ClientReferenziertKeinModul("Zeltlotse.App", Fixture("ClientMitModul")).ToList();

        Assert.Equal(3, verstoesse.Count);
        Assert.Contains(verstoesse, v => v.Contains("Zeltlotse.Personen.Contracts", StringComparison.Ordinal));
        Assert.Contains(verstoesse, v => v.Contains("Zeltlotse.SharedKernel", StringComparison.Ordinal));
    }

    [Fact]
    public void Ui_Komponenten_hat_nur_die_zwei_erlaubten_Pakete_und_keine_Projektreferenz()
    {
        var projekt = Projekt(ClientRegeln.Komponenten);

        Assert.Empty(ClientRegeln.KomponentenOhneFremdeReferenzen(projekt));
        Assert.Equal(
            ["Microsoft.AspNetCore.Components", "Microsoft.AspNetCore.Components.Web"],
            ProjektDateiRegeln.PaketReferenzen(projekt).Order());
    }

    [Fact]
    public void Negativ_Ui_Komponenten_mit_Projektreferenz_und_fremdem_Paket_wird_erkannt()
    {
        var verstoesse = ClientRegeln.KomponentenOhneFremdeReferenzen(Fixture("KomponentenMitReferenzen")).ToList();

        Assert.Equal(2, verstoesse.Count);
        Assert.Contains(verstoesse, v => v.Contains("Zeltlotse.Api.Contracts", StringComparison.Ordinal));
        Assert.Contains(verstoesse, v => v.Contains("Microsoft.AspNetCore.Components.WebAssembly", StringComparison.Ordinal));
    }

    [Fact]
    public void Ui_referenziert_nur_Ui_Komponenten_und_Api_Contracts()
    {
        Assert.Equal(
            ["Zeltlotse.Api.Contracts", "Zeltlotse.Ui.Komponenten"],
            ProjektDateiRegeln.ProjektReferenzen(Projekt(ClientRegeln.Ui)).Order());
    }

    [Theory]
    [MemberData(nameof(Clients))]
    public void Clients_haben_keine_Pakete_ausserhalb_von_Microsoft(string client)
    {
        Assert.Empty(ClientRegeln.ClientPaketeNurMicrosoft(client, Projekt(client)));
    }

    [Fact]
    public void Negativ_Client_mit_Fremdpaket_wird_erkannt()
    {
        Assert.Equal(
            ["Zeltlotse.App referenziert das Paket MudBlazor; Clients nutzen nur Pakete von Microsoft."],
            ClientRegeln.ClientPaketeNurMicrosoft("Zeltlotse.App", Fixture("ClientMitFremdpaket")));
    }

    [Fact]
    public void Public_verwendet_keinen_NurInteraktiv_Baustein()
    {
        ClientRegeln.OhneNurInteraktiv(ClientRegeln.Public).Check(AbhaengigkeitsRegeln.Architektur);
    }

    [Fact]
    public void Negativ_Public_Typ_mit_NurInteraktiv_Baustein_wird_erkannt()
    {
        var regel = ClientRegeln.OhneNurInteraktiv(typeof(ClientTests).Assembly.GetName().Name!);

        var verstoesse = regel.Evaluate(ClientNegativ).Where(e => !e.Passed).ToList();

        Assert.Contains(verstoesse, v => v.EvaluatedObjectIdentifier.Identifier.Contains("StatischeSeiteMitInteraktivemBaustein", StringComparison.Ordinal));
    }

    [Fact]
    public void Baustein_mit_EventCallback_Parameter_traegt_NurInteraktiv()
    {
        var typen = new[] { ClientRegeln.Komponenten, ClientRegeln.Ui, ClientRegeln.App, ClientRegeln.Public }
            .SelectMany(a => Loesung.AssemblyVon(a)!.GetTypes());

        Assert.Empty(ClientRegeln.EventCallbackNurMitNurInteraktiv(typen));
    }

    [Fact]
    public void Komponenten_enthalten_interaktive_Bausteine_damit_die_Regel_greift()
    {
        var komponenten = Loesung.AssemblyVon(ClientRegeln.Komponenten)!.GetTypes();

        Assert.Contains(komponenten, t => t.Name == "SpeicherHinweis" && t.GetCustomAttributes(false).Any(a => a.GetType().Name == "NurInteraktivAttribute"));
        Assert.Contains(komponenten, t => t.Name == "InlineBestaetigung" && t.GetCustomAttributes(false).Any(a => a.GetType().Name == "NurInteraktivAttribute"));
    }

    [Fact]
    public void Negativ_EventCallback_ohne_NurInteraktiv_wird_erkannt()
    {
        var verstoesse = ClientRegeln.EventCallbackNurMitNurInteraktiv(typeof(ClientTests).Assembly.GetTypes()).ToList();

        var verstoss = Assert.Single(verstoesse);
        Assert.Contains("BausteinMitEventCallbackOhneMarke.Gewaehlt", verstoss, StringComparison.Ordinal);
    }

    [Fact]
    public void Nur_die_Umsetzung_von_IBrowser_haengt_von_JSInterop_ab()
    {
        ClientRegeln.NurBrowserNutztJsInterop(AbhaengigkeitsRegeln.Architektur, [.. ClientRegeln.Clients])
            .Check(AbhaengigkeitsRegeln.Architektur);
    }

    [Fact]
    public void Die_Umsetzung_von_IBrowser_nutzt_JSInterop_damit_die_Regel_greift()
    {
        var app = Loesung.AssemblyVon(ClientRegeln.App)!;
        var umsetzung = Assert.Single(app.GetTypes(), t => !t.IsInterface && t.GetInterfaces().Any(i => i.Name == "IBrowser"));

        Assert.Contains(umsetzung.GetConstructors().SelectMany(c => c.GetParameters()), p => p.ParameterType.FullName == "Microsoft.JSInterop.IJSRuntime");
    }

    [Fact]
    public void Negativ_JSInterop_ohne_IBrowser_wird_erkannt()
    {
        var regel = ClientRegeln.NurBrowserNutztJsInterop(ClientNegativ, typeof(ClientTests).Assembly.GetName().Name!);

        var verstoesse = regel.Evaluate(ClientNegativ).Where(e => !e.Passed).ToList();

        Assert.Contains(verstoesse, v => v.EvaluatedObjectIdentifier.Identifier.Contains("JsNutzerOhneBrowser", StringComparison.Ordinal));
    }

    [Fact]
    public void Razor_und_HTML_Dateien_unter_src_ohne_Inline_Stil_und_Inline_Skript_und_Bibliotheksklassen_nur_in_Ui_Komponenten()
    {
        var src = Path.Combine(Loesung.Wurzel, "src");
        var dateien = Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
            .Where(d => d.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) || d.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            .Where(d => !d.Split(Path.DirectorySeparatorChar).Any(teil => teil is "bin" or "obj"))
            .ToList();

        var verstoesse = dateien.SelectMany(d => ClientRegeln.QuelltextVerstoesse(ProjektVon(src, d), Path.GetRelativePath(src, d), File.ReadAllText(d)));

        Assert.Contains(dateien, d => d.EndsWith("index.html", StringComparison.Ordinal));
        Assert.Contains(dateien, d => d.EndsWith("Knopf.razor", StringComparison.Ordinal));
        Assert.Empty(verstoesse);
    }

    [Fact]
    public void Negativ_Razor_Datei_mit_Stil_Skript_und_Bibliotheksklassen_wird_erkannt()
    {
        var inhalt = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Negativ", "MitStil.razor.txt"));

        var verstoesse = ClientRegeln.QuelltextVerstoesse("Zeltlotse.App", "MitStil.razor", inhalt).ToList();

        Assert.Equal(5, verstoesse.Count);
        Assert.Contains("MitStil.razor: style-Attribut (CSP).", verstoesse);
        Assert.Contains("MitStil.razor: style-Block (CSP).", verstoesse);
        Assert.Contains("MitStil.razor: script ohne src (CSP).", verstoesse);
        Assert.Contains("MitStil.razor: Klasse elkw-surface außerhalb von Zeltlotse.Ui.Komponenten.", verstoesse);
        Assert.Contains("MitStil.razor: Klasse zl-offline-leiste außerhalb von Zeltlotse.Ui.Komponenten.", verstoesse);
    }

    [Fact]
    public void Negativ_Bibliotheksklassen_sind_in_Ui_Komponenten_erlaubt_Stil_aber_nicht()
    {
        var verstoesse = ClientRegeln.QuelltextVerstoesse(ClientRegeln.Komponenten, "X.razor", "<div class=\"elkw-surface\" style=\"color:red\"></div>").ToList();

        Assert.Equal(["X.razor: style-Attribut (CSP)."], verstoesse);
    }

    private static readonly ArchUnitNET.Domain.Architecture ClientNegativ = new ArchUnitNET.Loader.ArchLoader()
        .LoadAssemblies(typeof(ClientTests).Assembly, typeof(Ui.Komponenten.NurInteraktivAttribute).Assembly)
        .Build();

    private static string ProjektVon(string src, string datei) =>
        Path.GetRelativePath(src, datei).Split(Path.DirectorySeparatorChar)[0];

    private static XDocument Projekt(string projekt) =>
        XDocument.Load(Path.Combine(Loesung.Wurzel, "src", projekt, projekt + ".csproj"));

    private static XDocument Fixture(string name) =>
        XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Negativ", name + ".csproj.xml"));
}
