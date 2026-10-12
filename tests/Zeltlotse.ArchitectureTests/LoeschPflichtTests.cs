using Zeltlotse.ArchitectureTests.Negativ;

namespace Zeltlotse.ArchitectureTests;

/// <summary>AD-16: Datenauskunft und Empfänger für <c>PersonGeloescht</c> in jedem Modul mit Personenbezug, mit Negativ-Fixture.</summary>
public sealed class LoeschPflichtTests
{
    [Fact]
    public void Jedes_Modul_mit_Personenbezug_hat_eine_Datenauskunft_und_ausser_Personen_einen_Empfaenger_fuer_PersonGeloescht()
    {
        var verstoesse = Loesung.Module
            .SelectMany(m => LoeschPflichtRegeln.Verstoesse(m.Name, Loesung.AssemblyVon(m.Projekt)!.GetTypes(), loestPersonGeloeschtAus: m.Name == "Personen"))
            .ToList();

        Assert.Empty(verstoesse);
    }

    [Fact]
    public void Die_Regel_erkennt_den_Personenbezug_der_heutigen_Module()
    {
        var mitBezug = Loesung.Module
            .Where(m => LoeschPflichtRegeln.EntitaetenMitPersonId(Loesung.AssemblyVon(m.Projekt)!.GetTypes()).Count > 0)
            .Select(m => m.Name);

        Assert.Equal(["Konten", "Personen", "Organisationen", "Freizeiten"], mitBezug);
    }

    [Fact]
    public void Negativ_Modul_mit_PersonId_ohne_Auskunft_und_ohne_Empfaenger_wird_erkannt()
    {
        var verstoesse = LoeschPflichtRegeln.Verstoesse("Negativ", [typeof(NegativTeilnahmeDbContext), typeof(NegativTeilnahme)], loestPersonGeloeschtAus: false).ToList();

        Assert.Equal(
            [
                "Negativ: Entitäten mit PersonId, aber keine Umsetzung von IDatenauskunft.",
                "Negativ: Entitäten mit PersonId, aber kein Empfänger für PersonGeloescht.",
            ],
            verstoesse);
    }

    [Fact]
    public void Negativ_Modul_mit_Auskunft_und_Empfaenger_ist_regelkonform()
    {
        Type[] typen = [typeof(NegativTeilnahmeDbContext), typeof(NegativTeilnahme), typeof(NegativAuskunft), typeof(NegativPersonLoeschen)];

        Assert.Empty(LoeschPflichtRegeln.Verstoesse("Negativ", typen, loestPersonGeloeschtAus: false));
    }
}
