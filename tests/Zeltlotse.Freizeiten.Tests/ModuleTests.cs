using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Freizeiten.Domaene;
using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Freizeiten.Tests;

/// <summary>
/// Story 3-7: Module einer Freizeit (FR-44, AD-19, AD-20). Eine neue Freizeit nutzt keines; Schalten folgt der Konfliktregel zum alten
/// Wert desselben Moduls und ändert nur diesen Schalter, keine anderen Daten der Freizeit.
/// </summary>
public sealed class ModuleTests
{
    private static Freizeit NeueFreizeit() => Freizeit.Anlegen(
        Guid.CreateVersion7(), Guid.CreateVersion7(), "Zeltlager", new DateOnly(2027, 8, 2), new DateOnly(2027, 8, 13), 30, Guid.CreateVersion7(),
        new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Eine_neue_Freizeit_nutzt_kein_Modul()
    {
        var freizeit = NeueFreizeit();

        Assert.All(Enum.GetValues<ModulFeld>(), feld => Assert.Equal("false", freizeit.Modul(feld)));
        Assert.Empty(freizeit.Schalter());
    }

    [Fact]
    public void Einschalten_mit_passendem_altem_Wert_schaltet_nur_dieses_Modul()
    {
        var freizeit = NeueFreizeit();

        Assert.True(freizeit.ModulAendern(ModulFeld.Finanzen, "false", "true"));

        Assert.Equal(("false", "true", "false"), (freizeit.Modul(ModulFeld.Verpflegung), freizeit.Modul(ModulFeld.Finanzen), freizeit.Modul(ModulFeld.TeilnehmerZugang)));
        Assert.Equal([FunktionsSchalter.Finanzen], freizeit.Schalter());
    }

    [Fact]
    public void Die_Schalter_stehen_in_Enum_Reihenfolge()
    {
        var freizeit = NeueFreizeit();

        foreach (var feld in new[] { ModulFeld.TeilnehmerZugang, ModulFeld.Verpflegung, ModulFeld.Finanzen })
        {
            Assert.True(freizeit.ModulAendern(feld, "false", "true"));
        }

        Assert.Equal([FunktionsSchalter.Verpflegung, FunktionsSchalter.Finanzen, FunktionsSchalter.TeilnehmerZugang], freizeit.Schalter());
    }

    [Theory]
    [InlineData("true")]
    [InlineData(null)]
    [InlineData("")]
    public void Ein_abweichender_alter_Wert_ist_ein_Konflikt_und_aendert_nichts(string? alterWert)
    {
        var freizeit = NeueFreizeit();

        Assert.False(freizeit.ModulAendern(ModulFeld.Verpflegung, alterWert, "true"));

        Assert.Empty(freizeit.Schalter());
    }

    [Fact]
    public void An_und_wieder_aus_laesst_Freizeitseite_und_Fragen_unveraendert()
    {
        var freizeit = NeueFreizeit();
        Assert.True(freizeit.FeldAendern(FreizeitseiteFeld.Ort, null, "Kapf"));
        Assert.True(freizeit.FragenAendern(FragenFeld.Teilnehmende, null, "allergien"));

        Assert.True(freizeit.ModulAendern(ModulFeld.Verpflegung, "false", "true"));
        Assert.True(freizeit.ModulAendern(ModulFeld.Verpflegung, "true", "false"));

        Assert.Equal(("Kapf", "allergien"), (freizeit.Ort, freizeit.Fragen(FragenFeld.Teilnehmende)));
        Assert.Empty(freizeit.Schalter());
    }
}
