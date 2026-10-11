using System.Reflection;
using ArchUnitNET.xUnitV3;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.ArchitectureTests.Negativ;

namespace Zeltlotse.ArchitectureTests;

/// <summary>AD-14, AD-22, AD-24: Verschlüsselung, Protokollierung und Schema <c>betrieb</c>, jeweils mit Negativ-Fixture.</summary>
public sealed class DatenschutzTests
{
    private static readonly string[] Quellcode =
        [.. Loesung.Assemblies.Select(a => a.GetName().Name!).Where(n => n != "Zeltlotse.Testmodul")];

    private static readonly string NegativAssembly = typeof(DatenschutzTests).Assembly.GetName().Name!;

    [Fact]
    public void Besonders_nur_an_Geschuetzt_und_jedes_Geschuetzt_traegt_Besonders()
    {
        var typen = Loesung.Assemblies.SelectMany(a => a.GetTypes());

        Assert.Empty(DatenschutzRegeln.BesondersNurAlsGeschuetzt(typen));
    }

    [Fact]
    public void Testmodul_hat_eine_Besonders_Eigenschaft_damit_die_Regel_greift()
    {
        var notiz = Loesung.AssemblyVon("Zeltlotse.Testmodul")!.GetType("Zeltlotse.Testmodul.Notizen.Notiz", throwOnError: true)!;

        Assert.Contains(
            notiz.GetProperties(BindingFlags.Public | BindingFlags.Instance),
            p => p.IsDefined(typeof(SharedKernel.Datenschutz.BesondersAttribute)));
    }

    [Fact]
    public void Negativ_Besonders_an_string_wird_erkannt()
    {
        var verstoesse = DatenschutzRegeln.BesondersNurAlsGeschuetzt([typeof(BesondersAlsString)]).ToList();

        Assert.Equal([$"{typeof(BesondersAlsString).FullName}.Allergie trägt [Besonders], ist aber String statt Geschuetzt."], verstoesse);
    }

    [Fact]
    public void Negativ_Geschuetzt_ohne_Besonders_wird_erkannt()
    {
        Assert.Single(DatenschutzRegeln.BesondersNurAlsGeschuetzt([typeof(GeschuetztOhneBesonders)]));
    }

    [Fact]
    public void Kein_Aufruf_von_LoggerExtensions_im_Quellcode()
    {
        DatenschutzRegeln.KeineLoggerExtensions(Quellcode).Check(AbhaengigkeitsRegeln.Architektur);
    }

    [Fact]
    public void Negativ_LogInformation_wird_erkannt()
    {
        var verstoesse = DatenschutzRegeln.KeineLoggerExtensions(NegativAssembly)
            .Evaluate(AbhaengigkeitsRegeln.Negativ).Where(e => !e.Passed).ToList();

        Assert.Contains(verstoesse, v => v.EvaluatedObjectIdentifier.Identifier.Contains(nameof(DirektLogger), StringComparison.Ordinal));
        Assert.DoesNotContain(verstoesse, v => v.EvaluatedObjectIdentifier.Identifier.Contains(nameof(QuellgenerierterLogger), StringComparison.Ordinal));
    }

    [Fact]
    public void Kein_EnableSensitiveDataLogging_oder_EnableDetailedErrors_im_Quellcode()
    {
        DatenschutzRegeln.KeineSensiblenEfProtokolle(Quellcode).Check(AbhaengigkeitsRegeln.Architektur);
    }

    [Fact]
    public void Negativ_EnableSensitiveDataLogging_und_EnableDetailedErrors_werden_erkannt()
    {
        var verstoesse = DatenschutzRegeln.KeineSensiblenEfProtokolle(NegativAssembly)
            .Evaluate(AbhaengigkeitsRegeln.Negativ).Where(e => !e.Passed).ToList();

        Assert.Contains(verstoesse, v => v.EvaluatedObjectIdentifier.Identifier.Contains(nameof(SensiblesProtokoll), StringComparison.Ordinal));
        Assert.Contains(verstoesse, v => v.EvaluatedObjectIdentifier.Identifier.Contains(nameof(DetaillierteFehler), StringComparison.Ordinal));
    }

    [Fact]
    public void BetriebDbContext_liegt_nur_im_Schema_betrieb()
    {
        var fabrik = Loesung.AssemblyVon("Zeltlotse.Api")!.GetType("Zeltlotse.Api.Betrieb.BetriebDbContextFabrik", throwOnError: true)!;
        var erzeugen = fabrik.GetMethod("CreateDbContext")!;
        using var db = (DbContext)erzeugen.Invoke(Activator.CreateInstance(fabrik, nonPublic: true), [Array.Empty<string>()])!;

        Assert.NotEmpty(db.Model.GetEntityTypes());
        Assert.Empty(DatenschutzRegeln.FremdeSchemas(db, "betrieb"));
    }

    [Fact]
    public void Negativ_Tabelle_ausserhalb_von_betrieb_wird_erkannt()
    {
        using var db = new NegativBetriebDbContext();

        var verstoesse = DatenschutzRegeln.FremdeSchemas(db, "betrieb").ToList();

        Assert.Contains(verstoesse, v => v.Contains("personen.fremd", StringComparison.Ordinal));
    }
}
