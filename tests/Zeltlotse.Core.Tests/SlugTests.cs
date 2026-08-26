using Zeltlotse.Core.Organisationen;

namespace Zeltlotse.Core.Tests;

public class SlugTests
{
    [Theory]
    [InlineData("Ev. Kirchengemeinde Musterstadt", "ev-kirchengemeinde-musterstadt")]
    [InlineData("Bezirksjugendwerk Öhringen", "bezirksjugendwerk-oehringen")]
    [InlineData("Groß-Umstadt", "gross-umstadt")]
    [InlineData("CVJM Württemberg", "cvjm-wuerttemberg")]
    [InlineData("  Doppelte   Leerzeichen  ", "doppelte-leerzeichen")]
    [InlineData("Sonder!!!zeichen???", "sonder-zeichen")]
    public void Erzeugt_lesbare_Adresse_aus_dem_Namen(string name, string erwartet)
    {
        Assert.Equal(erwartet, Slug.AusName(name));
    }

    [Fact]
    public void Kuerzt_sehr_lange_Namen_ohne_Bindestrich_am_Ende()
    {
        var slug = Slug.AusName(new string('a', 40) + " " + new string('b', 40));

        Assert.True(slug.Length <= 60, $"Slug ist {slug.Length} Zeichen lang");
        Assert.False(slug.EndsWith('-'), "Slug endet auf einem Bindestrich");
    }

    [Fact]
    public void Faellt_auf_Ersatzwert_zurueck_wenn_nichts_uebrig_bleibt()
    {
        Assert.Equal("organisation", Slug.AusName("!!!"));
    }

    [Fact]
    public void Haengt_eine_Zahl_an_wenn_die_Adresse_belegt_ist()
    {
        var belegt = new HashSet<string>(["gemeinde", "gemeinde-2"]);

        Assert.Equal("gemeinde-3", Slug.Eindeutig("Gemeinde", belegt.Contains));
    }
}
