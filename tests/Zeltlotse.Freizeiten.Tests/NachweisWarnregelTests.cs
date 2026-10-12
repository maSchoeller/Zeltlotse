using Microsoft.Extensions.Time.Testing;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Freizeiten.Domaene;
using Zeltlotse.Freizeiten.Nachweise;
using Zeltlotse.Organisationen.Contracts.Nachweise;
using StandArt = Zeltlotse.Organisationen.Contracts.Nachweise.NachweisArt;
using WarnArt = Zeltlotse.Api.Contracts.Freizeiten.NachweisArt;

namespace Zeltlotse.Freizeiten.Tests;

/// <summary>
/// Warnregel für Pflicht-Nachweise (FR-11, Story 1-6) als reine Funktion mit künstlicher Uhr: fehlt, läuft vor dem letzten
/// Freizeittag ab, abgelaufen; „gültig bis“ zählt einschließlich, „heute“ ist der Kalendertag in Europe/Berlin.
/// </summary>
public sealed class NachweisWarnregelTests
{
    private static readonly DateOnly Letzter = new(2027, 8, 13);
    private static readonly Guid Jonas = Guid.Parse("0199b000-0000-7000-8000-0000000000a1");
    private static readonly Guid PersonB = Guid.Parse("0199b000-0000-7000-8000-0000000000b1");
    private static readonly StandArt[] Pflicht = [StandArt.Fuehrungszeugnis, StandArt.ErsteHilfe];

    private static DateOnly Heute(DateTimeOffset utc) => FreizeitZeiten.Heute(new FakeTimeProvider(utc).GetUtcNow());

    private static readonly DateOnly Heute20261012 = Heute(new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Matrix_Warnregel_fehlt_laeuft_ab_und_abgelaufen()
    {
        NachweisGueltigkeit[] bestaetigungen =
        [
            new(Jonas, StandArt.ErsteHilfe, new DateOnly(2027, 4, 15)),
            new(PersonB, StandArt.ErsteHilfe, new DateOnly(2027, 8, 13)),
            new(PersonB, StandArt.Fuehrungszeugnis, new DateOnly(2026, 10, 11)),
        ];

        Assert.Equal(
            [new NachweisWarnung(WarnArt.Fuehrungszeugnis, NachweisWarnungGrund.Fehlt, null), new NachweisWarnung(WarnArt.ErsteHilfe, NachweisWarnungGrund.LaeuftAb, new DateOnly(2027, 4, 15))],
            NachweisWarnregel.Warnungen(Pflicht, bestaetigungen.Where(b => b.PersonId == Jonas), Letzter, Heute20261012));
        Assert.Equal(
            [new NachweisWarnung(WarnArt.Fuehrungszeugnis, NachweisWarnungGrund.Abgelaufen, new DateOnly(2026, 10, 11))],
            NachweisWarnregel.Warnungen(Pflicht, bestaetigungen.Where(b => b.PersonId == PersonB), Letzter, Heute20261012));
    }

    [Fact]
    public void Ohne_Warnung_wenn_alle_Pflicht_Arten_bis_nach_dem_letzten_Tag_gelten_und_JuLeiCa_keine_Pflicht_ist()
    {
        NachweisGueltigkeit[] bestaetigungen =
        [
            new(Jonas, StandArt.Fuehrungszeugnis, new DateOnly(2030, 11, 3)),
            new(Jonas, StandArt.ErsteHilfe, new DateOnly(2027, 8, 14)),
        ];

        Assert.Empty(NachweisWarnregel.Warnungen(Pflicht, bestaetigungen, Letzter, Heute20261012));
    }

    [Fact]
    public void Gueltig_bis_gilt_einschliesslich_am_letzten_Tag_und_am_heutigen_Tag()
    {
        Assert.Empty(NachweisWarnregel.Warnungen([StandArt.ErsteHilfe], [new(Jonas, StandArt.ErsteHilfe, Letzter)], Letzter, Heute20261012));
        Assert.Equal(
            [new NachweisWarnung(WarnArt.ErsteHilfe, NachweisWarnungGrund.LaeuftAb, Heute20261012)],
            NachweisWarnregel.Warnungen([StandArt.ErsteHilfe], [new(Jonas, StandArt.ErsteHilfe, Heute20261012)], Letzter, Heute20261012));
    }

    [Fact]
    public void Nach_der_Freizeit_keine_Warnung_fuer_eine_Bestaetigung_bis_nach_dem_letzten_Tag_auch_wenn_sie_vor_heute_endet()
    {
        var nachDemEnde = Heute(new DateTimeOffset(2027, 9, 1, 8, 0, 0, TimeSpan.Zero));

        Assert.Empty(NachweisWarnregel.Warnungen([StandArt.ErsteHilfe], [new(Jonas, StandArt.ErsteHilfe, new DateOnly(2027, 8, 20))], Letzter, nachDemEnde));
    }

    [Fact]
    public void Heute_ist_der_Kalendertag_in_Berlin()
    {
        // 11.10.2026 22:30 UTC = 12.10.2026 00:30 MESZ.
        Assert.Equal(new DateOnly(2026, 10, 12), Heute(new DateTimeOffset(2026, 10, 11, 22, 30, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2026, 10, 11), Heute(new DateTimeOffset(2026, 10, 11, 21, 59, 0, TimeSpan.Zero)));
    }
}
