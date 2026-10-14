using Zeltlotse.Freizeiten.Contracts.Events;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Freizeiten.Domaene;

namespace Zeltlotse.Freizeiten.Tests;

/// <summary>
/// Story 3-8: Abschluss einer Freizeit (AD-17, AD-28). Nur in der Nachbereitung; danach ist sie gelaufen, und das Ereignis
/// <see cref="FreizeitAbgeschlossen"/> liegt genau einmal bereit.
/// </summary>
public sealed class AbschlussTests
{
    private static readonly DateTimeOffset Angelegt = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>03.–08.10.2026; Freizeitende ist der 09.10.2026, 00:00 Uhr in Berlin (08.10., 22:00 Uhr UTC).</summary>
    private static Freizeit Herbstfreizeit() => Freizeit.Anlegen(
        Guid.CreateVersion7(), Guid.CreateVersion7(), "Herbstfreizeit 2026", new DateOnly(2026, 10, 3), new DateOnly(2026, 10, 8), 30, Guid.CreateVersion7(),
        Angelegt);

    [Fact]
    public void Abschliessen_in_der_Nachbereitung_setzt_den_Zeitpunkt_und_legt_das_Ereignis_ab()
    {
        var freizeit = Herbstfreizeit();
        var jetzt = new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

        Assert.True(freizeit.Abschliessen(jetzt));

        Assert.Equal(jetzt, freizeit.AbgeschlossenAm);
        Assert.Equal(FreizeitPhase.Gelaufen, freizeit.Phase(jetzt));
        var ereignis = Assert.IsType<FreizeitAbgeschlossen>(Assert.Single(freizeit.Ereignisse));
        Assert.Equal(freizeit.Id, ereignis.FreizeitId);
        Assert.Equal(1, freizeit.Folgenummer);
    }

    [Theory]
    [InlineData("2026-10-02T21:59:00+00:00")] // in Vorbereitung: 02.10., 23:59 Uhr in Berlin
    [InlineData("2026-10-08T21:59:00+00:00")] // läuft: letzter Tag, 23:59 Uhr in Berlin
    public void Vor_dem_Freizeitende_geht_der_Abschluss_nicht(string zeitpunkt)
    {
        var freizeit = Herbstfreizeit();
        var jetzt = DateTimeOffset.Parse(zeitpunkt, System.Globalization.CultureInfo.InvariantCulture);

        Assert.False(freizeit.Abschliessen(jetzt));

        Assert.Null(freizeit.AbgeschlossenAm);
        Assert.Empty(freizeit.Ereignisse);
    }

    [Fact]
    public void Ab_Mitternacht_nach_dem_letzten_Tag_geht_der_Abschluss()
    {
        var freizeit = Herbstfreizeit();

        Assert.True(freizeit.Abschliessen(new DateTimeOffset(2026, 10, 8, 22, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Ein_zweiter_Abschluss_aendert_nichts()
    {
        var freizeit = Herbstfreizeit();
        var erster = new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);
        Assert.True(freizeit.Abschliessen(erster));

        Assert.False(freizeit.Abschliessen(erster.AddDays(1)));

        Assert.Equal(erster, freizeit.AbgeschlossenAm);
        Assert.Single(freizeit.Ereignisse);
    }
}
