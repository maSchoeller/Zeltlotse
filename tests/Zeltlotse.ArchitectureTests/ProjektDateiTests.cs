using System.Xml.Linq;

namespace Zeltlotse.ArchitectureTests;

/// <summary>Projekt-Referenzen nach AD-2, AD-5, AD-24 und AD-27, jeweils mit Negativ-Fixture.</summary>
public sealed class ProjektDateiTests
{
    public static TheoryData<string> ModulNamen => [.. Loesung.Module.Select(m => m.Name)];

    [Theory]
    [MemberData(nameof(ModulNamen))]
    public void Modul_referenziert_fremde_Module_nur_ueber_Contracts(string modul)
    {
        var projekt = $"Zeltlotse.{modul}";

        Assert.Empty(ProjektDateiRegeln.ModulReferenziertNurContracts(projekt, ModulProjekt(modul, projekt)));
    }

    [Theory]
    [MemberData(nameof(ModulNamen))]
    public void Modul_Contracts_referenzieren_kein_Modul_Projekt(string modul)
    {
        var projekt = $"Zeltlotse.{modul}.Contracts";

        Assert.Empty(ProjektDateiRegeln.ContractsReferenzierenKeinModul(projekt, ModulProjekt(modul, projekt)));
    }

    [Fact]
    public void Nur_der_API_Host_bindet_den_Mediator_Generator_ein()
    {
        var verstoesse = Directory
            .GetFiles(Path.Combine(Loesung.Wurzel, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(p => Path.GetFileNameWithoutExtension(p) != "Zeltlotse.Api")
            .SelectMany(p => ProjektDateiRegeln.KeinMediatorGenerator(Path.GetFileNameWithoutExtension(p), XDocument.Load(p)));

        Assert.Empty(verstoesse);
        Assert.Contains("Mediator.SourceGenerator", ProjektDateiRegeln.PaketReferenzen(
            XDocument.Load(Path.Combine(Loesung.Wurzel, "src", "Zeltlotse.Api", "Zeltlotse.Api.csproj"))));
    }

    [Fact]
    public void Api_Contracts_hat_keine_Referenzen()
    {
        var pfad = Path.Combine(Loesung.Wurzel, "src", "Zeltlotse.Api.Contracts", "Zeltlotse.Api.Contracts.csproj");

        Assert.Empty(ProjektDateiRegeln.ApiContractsOhneReferenzen("Zeltlotse.Api.Contracts", XDocument.Load(pfad)));
    }

    [Fact]
    public void Negativ_fremdes_Modul_Projekt_wird_erkannt()
    {
        var verstoesse = ProjektDateiRegeln.ModulReferenziertNurContracts("Zeltlotse.Programm", Fixture("ProgrammReferenziertKueche"));

        Assert.Equal(["Zeltlotse.Programm referenziert Zeltlotse.Kueche; erlaubt sind nur SharedKernel, Api.Contracts und Modul-Contracts."], verstoesse);
    }

    [Fact]
    public void Negativ_Mediator_Generator_im_Modul_wird_erkannt()
    {
        Assert.NotEmpty(ProjektDateiRegeln.KeinMediatorGenerator("Zeltlotse.Konten", Fixture("KontenMitMediatorGenerator")));
    }

    [Fact]
    public void Negativ_Api_Contracts_mit_Referenz_wird_erkannt()
    {
        Assert.NotEmpty(ProjektDateiRegeln.ApiContractsOhneReferenzen("Zeltlotse.Api.Contracts", Fixture("ApiContractsMitReferenz")));
    }

    [Fact]
    public void Es_gibt_genau_die_zehn_Module()
    {
        var ordner = Directory.GetDirectories(Path.Combine(Loesung.Wurzel, "src", "Modules"))
            .Select(Path.GetFileName)
            .Order();

        Assert.Equal(Loesung.Module.Select(m => m.Name).Order(), ordner);
    }

    private static XDocument ModulProjekt(string modul, string projekt) =>
        XDocument.Load(Path.Combine(Loesung.Wurzel, "src", "Modules", modul, projekt, projekt + ".csproj"));

    private static XDocument Fixture(string name) =>
        XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Negativ", name + ".csproj.xml"));
}
