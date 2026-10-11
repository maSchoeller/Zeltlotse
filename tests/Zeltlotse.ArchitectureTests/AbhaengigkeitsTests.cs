using ArchUnitNET.xUnitV3;

namespace Zeltlotse.ArchitectureTests;

public sealed class AbhaengigkeitsTests
{
    private static readonly string[] ModulUndTestmodul =
        [.. Loesung.Module.Select(m => m.Projekt), "Zeltlotse.Testmodul"];

    public static TheoryData<string> ModulNamen => [.. Loesung.Module.Select(m => m.Name)];

    [Fact]
    public void Nur_Command_Handler_haengen_vom_schreibenden_DbContext_ab()
    {
        AbhaengigkeitsRegeln.NurHandlerNutzenSchreibKontext(AbhaengigkeitsRegeln.Architektur, ModulUndTestmodul)
            .Check(AbhaengigkeitsRegeln.Architektur);
    }

    [Fact]
    public void Negativ_Endpunkt_mit_schreibendem_DbContext_wird_erkannt()
    {
        var regel = AbhaengigkeitsRegeln.NurHandlerNutzenSchreibKontext(
            AbhaengigkeitsRegeln.Negativ, typeof(AbhaengigkeitsTests).Assembly.GetName().Name!);

        var verstoesse = regel.Evaluate(AbhaengigkeitsRegeln.Negativ).Where(e => !e.Passed).ToList();

        Assert.Contains(verstoesse, v => v.EvaluatedObjectIdentifier.Identifier.Contains("FalscherEndpunkt", StringComparison.Ordinal));
        Assert.DoesNotContain(verstoesse, v => v.EvaluatedObjectIdentifier.Identifier.EndsWith("NegativDbContext", StringComparison.Ordinal));
    }

    [Fact]
    public void Negativ_Endpunkt_Lambda_in_der_Modul_Registrierung_wird_erkannt()
    {
        var regel = AbhaengigkeitsRegeln.NurHandlerNutzenSchreibKontext(
            AbhaengigkeitsRegeln.Negativ, typeof(AbhaengigkeitsTests).Assembly.GetName().Name!);

        var verstoesse = regel.Evaluate(AbhaengigkeitsRegeln.Negativ).Where(e => !e.Passed).ToList();

        Assert.Contains(verstoesse, v => v.EvaluatedObjectIdentifier.Identifier.Contains("NegativModul", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(ModulNamen))]
    public void Contracts_Aufrufe_nur_entlang_der_Pfeile(string modul)
    {
        AbhaengigkeitsRegeln.ContractsNurEntlangDerPfeile(Loesung.Module.Single(m => m.Name == modul))
            .Check(AbhaengigkeitsRegeln.Architektur);
    }

    [Theory]
    [MemberData(nameof(ModulNamen))]
    public void Module_nutzen_in_Api_Contracts_nur_eigenen_Ordner_und_Gemeinsam(string modul)
    {
        AbhaengigkeitsRegeln.ApiContractsNurEigenerOrdner(Loesung.Module.Single(m => m.Name == modul))
            .Check(AbhaengigkeitsRegeln.Architektur);
    }

    [Fact]
    public void Handler_rufen_weder_Publish_noch_die_Outbox_direkt_auf()
    {
        AbhaengigkeitsRegeln.HandlerOhnePublishUndOutbox(ModulUndTestmodul)
            .Check(AbhaengigkeitsRegeln.Architektur);
    }
}
