using Microsoft.Extensions.Time.Testing;
using Zeltlotse.Organisationen.Domaene;
using OrgRolleArt = Zeltlotse.SharedKernel.Rechte.OrgRolle;

namespace Zeltlotse.Organisationen.Tests;

/// <summary>Fachlogik von Organisation und Org-Einladung ohne Datenbank (AD-11, AD-28): Ablauf, einmal, Verwerfen, Mail-Bindung.</summary>
public sealed class OrgEinladungTests
{
    private readonly FakeTimeProvider _uhr = new(new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero));

    private OrgEinladung Neu(OrgRolleArt rolle = OrgRolleArt.OrgAdmin) =>
        OrgEinladung.Anlegen(Guid.CreateVersion7(), Guid.CreateVersion7(), "andrea@example.org", rolle, [1], Guid.CreateVersion7(), _uhr.GetUtcNow());

    [Fact]
    public void Annehmbar_bis_kurz_vor_7_Tagen_danach_abgelaufen()
    {
        var einladung = Neu();

        Assert.Equal(_uhr.GetUtcNow() + TimeSpan.FromDays(7), einladung.GueltigBis);
        _uhr.Advance(TimeSpan.FromDays(7) - TimeSpan.FromTicks(1));
        Assert.True(einladung.IstAnnehmbar(_uhr.GetUtcNow()));

        _uhr.Advance(TimeSpan.FromTicks(1));
        Assert.False(einladung.IstAnnehmbar(_uhr.GetUtcNow()));
        Assert.Throws<InvalidOperationException>(() => einladung.Annehmen(_uhr.GetUtcNow()));
        Assert.Equal(OrgEinladungStatus.Offen, einladung.Status);
    }

    [Fact]
    public void Nur_einmal_annehmbar()
    {
        var einladung = Neu();

        einladung.Annehmen(_uhr.GetUtcNow());

        Assert.Equal(OrgEinladungStatus.Angenommen, einladung.Status);
        Assert.False(einladung.IstAnnehmbar(_uhr.GetUtcNow()));
        Assert.Throws<InvalidOperationException>(() => einladung.Annehmen(_uhr.GetUtcNow()));
    }

    [Fact]
    public void Verworfen_ist_nicht_mehr_annehmbar_und_eine_angenommene_bleibt_angenommen()
    {
        var offen = Neu();
        var angenommen = Neu();
        angenommen.Annehmen(_uhr.GetUtcNow());

        offen.Verwerfen();
        angenommen.Verwerfen();

        Assert.Equal(OrgEinladungStatus.Verworfen, offen.Status);
        Assert.False(offen.IstAnnehmbar(_uhr.GetUtcNow()));
        Assert.Equal(OrgEinladungStatus.Angenommen, angenommen.Status);
    }

    [Theory]
    [InlineData("andrea@example.org", true)]
    [InlineData(" Andrea@Example.ORG ", true)]
    [InlineData("andrea@example.com", false)]
    [InlineData(null, false)]
    public void Gilt_nur_fuer_die_eingeladene_Mail_Adresse(string? kontoMail, bool gilt) =>
        Assert.Equal(gilt, Neu().GiltFuer(kontoMail));

    [Fact]
    public void Organisation_wird_mit_dem_ersten_Einrichten_eingerichtet_und_bleibt_es()
    {
        var organisation = Organisation.Anlegen(Guid.CreateVersion7(), "  Jugendwerk Musterstadt ", _uhr.GetUtcNow());
        Assert.Equal("Jugendwerk Musterstadt", organisation.Name);
        Assert.Null(organisation.EingerichtetAm);

        var erstes = _uhr.GetUtcNow();
        organisation.Einrichten(erstes);
        _uhr.Advance(TimeSpan.FromDays(1));
        organisation.Einrichten(_uhr.GetUtcNow());

        Assert.Equal(erstes, organisation.EingerichtetAm);
    }
}
