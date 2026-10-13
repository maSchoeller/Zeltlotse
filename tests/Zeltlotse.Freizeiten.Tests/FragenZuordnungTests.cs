using Zeltlotse.Freizeiten.Contracts.Events;
using Zeltlotse.Freizeiten.Domaene;
using Zeltlotse.Personen.Contracts.Freizeitpass;

namespace Zeltlotse.Freizeiten.Tests;

/// <summary>
/// Story 2-5, AD-4, AD-8: Das Enum <see cref="FreizeitpassFrage"/> im Events-Namensraum spiegelt den festen Fragenkatalog eineindeutig.
/// Jede Katalog-Kennung hat genau ein Mitglied (PascalCase der Kennung), jedes Mitglied genau eine Kennung; Werte ab 1 ohne Lücke.
/// </summary>
public sealed class FragenZuordnungTests
{
    [Fact]
    public void Jede_Katalogfrage_hat_genau_ein_Enum_Mitglied_und_umgekehrt()
    {
        var kennungen = Fragenkatalog.Fragen.Select(f => f.Kennung).ToList();

        var mitglieder = FragenImEreignis.Aus(kennungen);

        Assert.Equal(kennungen.Count, mitglieder.Length);
        Assert.Equal(kennungen.Count, mitglieder.Distinct().Count());
        Assert.Equal(Enum.GetValues<FreizeitpassFrage>().Order(), mitglieder.Order());
        Assert.Equal(kennungen, mitglieder.Select(FragenImEreignis.Kennung));
        Assert.Equal(Enumerable.Range(1, kennungen.Count), Enum.GetValues<FreizeitpassFrage>().Select(f => (int)f).Order());
    }

    [Fact]
    public void Der_Name_ist_die_PascalCase_Form_der_Kennung()
    {
        Assert.Equal(FreizeitpassFrage.ZeckenEntfernen, Assert.Single(FragenImEreignis.Aus(["zecken-entfernen"])));
        Assert.Equal(FreizeitpassFrage.Allergien, Assert.Single(FragenImEreignis.Aus(["allergien"])));
        Assert.Equal("zecken-entfernen", FragenImEreignis.Kennung(FreizeitpassFrage.ZeckenEntfernen));
        Assert.Equal(
            ["allergien", "zecken-entfernen", "hinweise"],
            Fragenkatalog.Fragen.Select(f => f.Kennung).Where(k => k is "allergien" or "zecken-entfernen" or "hinweise"));
    }

    [Fact]
    public void Eine_Freizeit_liefert_die_Fragen_der_Zielgruppe_der_Anmeldung()
    {
        var freizeit = Freizeit.Anlegen(Guid.CreateVersion7(), Guid.CreateVersion7(), "Z", new DateOnly(2027, 8, 2), new DateOnly(2027, 8, 13), 30, Guid.CreateVersion7(), DateTimeOffset.UtcNow);
        Assert.True(freizeit.FragenAendern(Zeltlotse.Api.Contracts.Freizeiten.FragenFeld.Teilnehmende, null, "schwimmen,allergien"));
        Assert.True(freizeit.FragenAendern(Zeltlotse.Api.Contracts.Freizeiten.FragenFeld.Mitarbeitende, null, "notfallkontakte"));

        Assert.Equal([FreizeitpassFrage.Allergien, FreizeitpassFrage.Schwimmen], FragenImEreignis.Aus(freizeit.FragenFuer(AnmeldeArt.Teilnehmende)));
        Assert.Equal([FreizeitpassFrage.Notfallkontakte], FragenImEreignis.Aus(freizeit.FragenFuer(AnmeldeArt.Mitarbeitende)));
    }
}
