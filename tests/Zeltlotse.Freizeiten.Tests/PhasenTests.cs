using Microsoft.Extensions.Time.Testing;
using Zeltlotse.Freizeiten.Contracts.Events;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Freizeiten.Domaene;

namespace Zeltlotse.Freizeiten.Tests;

/// <summary>
/// Phasen und Freigabefenster (AD-17, FR-45, FR-24d) mit künstlicher Uhr an allen Grenzen in Europe/Berlin, auch über die
/// Zeitumstellung; dazu <see cref="Freizeit.LeitungEinsetzen"/> ohne Datenbank.
/// </summary>
public sealed class PhasenTests
{
    private static readonly DateOnly Erster = new(2027, 8, 2);
    private static readonly DateOnly Letzter = new(2027, 8, 13);

    private static FreizeitPhase PhaseUm(DateOnly erster, DateOnly letzter, DateTimeOffset utc, DateTimeOffset? abgeschlossenAm = null)
    {
        var uhr = new FakeTimeProvider(utc);
        return FreizeitZeiten.Phase(erster, letzter, abgeschlossenAm, uhr.GetUtcNow());
    }

    private static DateTimeOffset Utc(int jahr, int monat, int tag, int stunde, int minute) => new(jahr, monat, tag, stunde, minute, 0, TimeSpan.Zero);

    [Fact]
    public void Sommer_Vortag_2359_ist_in_Vorbereitung_erster_Tag_0000_laeuft_letzter_Tag_2359_laeuft_Tag_danach_0000_Nachbereitung()
    {
        // Sommerzeit: Berlin = UTC+2.
        Assert.Equal(FreizeitPhase.InVorbereitung, PhaseUm(Erster, Letzter, Utc(2027, 8, 1, 21, 59)));
        Assert.Equal(FreizeitPhase.Laeuft, PhaseUm(Erster, Letzter, Utc(2027, 8, 1, 22, 0)));
        Assert.Equal(FreizeitPhase.Laeuft, PhaseUm(Erster, Letzter, Utc(2027, 8, 13, 21, 59)));
        Assert.Equal(FreizeitPhase.Nachbereitung, PhaseUm(Erster, Letzter, Utc(2027, 8, 13, 22, 0)));
    }

    [Fact]
    public void Ueber_die_Umstellung_auf_Sommerzeit_gelten_die_Grenzen_in_Berlin()
    {
        // 28.03.2027: 02:00 MEZ → 03:00 MESZ. Beginn 00:00 MEZ (UTC+1), Ende 29.03. 00:00 MESZ (UTC+2).
        var erster = new DateOnly(2027, 3, 28);
        var letzter = new DateOnly(2027, 3, 28);

        Assert.Equal(FreizeitPhase.InVorbereitung, PhaseUm(erster, letzter, Utc(2027, 3, 27, 22, 59)));
        Assert.Equal(FreizeitPhase.Laeuft, PhaseUm(erster, letzter, Utc(2027, 3, 27, 23, 0)));
        Assert.Equal(FreizeitPhase.Laeuft, PhaseUm(erster, letzter, Utc(2027, 3, 28, 21, 59)));
        Assert.Equal(FreizeitPhase.Nachbereitung, PhaseUm(erster, letzter, Utc(2027, 3, 28, 22, 0)));
        Assert.Equal(Utc(2027, 3, 28, 22, 0), FreizeitZeiten.Freizeitende(letzter));
    }

    [Fact]
    public void Ueber_die_Umstellung_auf_Winterzeit_gelten_die_Grenzen_in_Berlin()
    {
        // 31.10.2027: 03:00 MESZ → 02:00 MEZ. Beginn 30.10. 00:00 MESZ (UTC+2), Ende 01.11. 00:00 MEZ (UTC+1).
        var erster = new DateOnly(2027, 10, 30);
        var letzter = new DateOnly(2027, 10, 31);

        Assert.Equal(FreizeitPhase.InVorbereitung, PhaseUm(erster, letzter, Utc(2027, 10, 29, 21, 59)));
        Assert.Equal(FreizeitPhase.Laeuft, PhaseUm(erster, letzter, Utc(2027, 10, 29, 22, 0)));
        Assert.Equal(FreizeitPhase.Laeuft, PhaseUm(erster, letzter, Utc(2027, 10, 31, 22, 59)));
        Assert.Equal(FreizeitPhase.Nachbereitung, PhaseUm(erster, letzter, Utc(2027, 10, 31, 23, 0)));
    }

    [Fact]
    public void Mit_Abschluss_ist_die_Freizeit_gelaufen_in_jeder_Lage_der_Uhr()
    {
        var abgeschlossen = Utc(2027, 8, 20, 10, 0);

        Assert.Equal(FreizeitPhase.Gelaufen, PhaseUm(Erster, Letzter, Utc(2027, 8, 21, 0, 0), abgeschlossen));
        Assert.Equal(FreizeitPhase.Gelaufen, PhaseUm(Erster, Letzter, Utc(2027, 8, 5, 0, 0), abgeschlossen));
    }

    [Fact]
    public void Freigabefenster_letzter_Tag_13_08_2027_Intervall_30_bis_12_09_gueltig_bis_13_09_0000_Berlin()
    {
        Assert.Equal(30, FreizeitZeiten.FreigabeIntervallVorgabeTage);
        Assert.Equal(new DateOnly(2027, 9, 12), FreizeitZeiten.FreigabeBis(Letzter, 30));
        Assert.Equal(Utc(2027, 9, 12, 22, 0), FreizeitZeiten.FreigabeGueltigBis(Letzter, 30));
        Assert.Equal(Utc(2027, 8, 13, 22, 0), FreizeitZeiten.Freizeitende(Letzter));
    }

    [Fact]
    public void Freigabefenster_ueber_die_Zeitumstellung_zaehlt_Kalendertage_in_Berlin()
    {
        // 15.10.2027 + 30 Tage: Ende 15.11.2027 00:00 MEZ (UTC+1), obwohl das Freizeitende noch in der Sommerzeit liegt.
        var letzter = new DateOnly(2027, 10, 15);

        Assert.Equal(new DateOnly(2027, 11, 14), FreizeitZeiten.FreigabeBis(letzter, 30));
        Assert.Equal(Utc(2027, 11, 14, 23, 0), FreizeitZeiten.FreigabeGueltigBis(letzter, 30));
        Assert.Equal(Utc(2027, 10, 15, 22, 0), FreizeitZeiten.Freizeitende(letzter));
    }

    [Fact]
    public void Leitung_einsetzen_legt_Rolle_und_Ereignis_ab_eine_zweite_Vergabe_derselben_Person_nichts()
    {
        var organisation = Guid.CreateVersion7();
        var person = Guid.CreateVersion7();
        var jetzt = Utc(2026, 7, 1, 8, 0);
        var freizeit = Freizeit.Anlegen(Guid.CreateVersion7(), organisation, " Zeltlager ", Erster, Letzter, 30, Guid.CreateVersion7(), jetzt);

        Assert.True(freizeit.LeitungEinsetzen(person, jetzt));
        Assert.False(freizeit.LeitungEinsetzen(person, jetzt.AddMinutes(1)));

        Assert.Equal("Zeltlager", freizeit.Name);
        var rolle = Assert.Single(freizeit.Rollen);
        Assert.Equal((organisation, freizeit.Id, person, SharedKernel.Rechte.FreizeitRolle.Leitung, jetzt), (rolle.OrganisationId, rolle.FreizeitId, rolle.PersonId, rolle.Rolle, rolle.VergebenAm));
        var ereignis = Assert.IsType<FreizeitRolleGeaendert>(Assert.Single(freizeit.Ereignisse));
        Assert.Equal(new FreizeitRolleGeaendert(freizeit.Id, person, FreizeitRolleArt.Leitung), ereignis);
        Assert.Equal(1, freizeit.Folgenummer);
    }

    [Fact]
    public void Phase_der_Freizeit_nutzt_ihren_Zeitraum_und_den_Abschluss()
    {
        var freizeit = Freizeit.Anlegen(Guid.CreateVersion7(), Guid.CreateVersion7(), "Zeltlager", Erster, Letzter, 30, Guid.CreateVersion7(), Utc(2026, 7, 1, 8, 0));

        Assert.Null(freizeit.AbgeschlossenAm);
        Assert.Equal(FreizeitPhase.InVorbereitung, freizeit.Phase(Utc(2026, 7, 1, 8, 0)));
        Assert.Equal(FreizeitPhase.Laeuft, freizeit.Phase(Utc(2027, 8, 5, 8, 0)));
        Assert.Equal(new FreizeitZeitpunkte(FreizeitPhase.Nachbereitung, Utc(2027, 8, 13, 22, 0), Utc(2027, 9, 12, 22, 0)), freizeit.Zeitpunkte(Utc(2027, 8, 20, 8, 0)));
    }
}
