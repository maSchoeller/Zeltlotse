using Microsoft.EntityFrameworkCore;
using Zeltlotse.Core.Freizeiten.Contracts;
using Zeltlotse.Core.Persistenz;

namespace Zeltlotse.Server.Integration.Tests;

[Collection(nameof(DatenbankSammlung))]
public class MandantentrennungTests(DatenbankFixture datenbank)
{
    /// <summary>
    /// Der Kern der Architektur: Selbst mit ausgeschaltetem EF-Filter darf die
    /// Datenbank keine fremde Zeile herausgeben. Fällt dieser Test, ist die
    /// Mandantentrennung nur noch eine Konvention.
    /// </summary>
    [Fact]
    public async Task Datenbank_haelt_fremde_Freizeiten_zurueck_auch_ohne_EF_Filter()
    {
        var (a, b) = await ZweiOrganisationenMitFreizeitAsync();

        await using var kontext = datenbank.Kontext(new MandantKontext
        {
            SichtbareOrganisationen = [a],
        });

        var sichtbar = await kontext.Freizeiten
            .IgnoreQueryFilters()
            .Select(f => f.TenantId)
            .ToListAsync();

        Assert.Equal([a], sichtbar.Distinct());
        Assert.DoesNotContain(b, sichtbar);
    }

    [Fact]
    public async Task Ohne_Organisationen_ist_keine_Freizeit_sichtbar()
    {
        await ZweiOrganisationenMitFreizeitAsync();

        await using var kontext = datenbank.Kontext(new MandantKontext
        {
            SichtbareOrganisationen = [],
        });

        Assert.Empty(await kontext.Freizeiten.IgnoreQueryFilters().ToListAsync());
    }

    /// <summary>
    /// Abnahmekriterium 7: Der Weg über eine fremde Adresse führt ins Leere,
    /// nicht zu Inhalten — hier auf der Ebene des EF-Filters geprüft.
    /// </summary>
    [Fact]
    public async Task Query_Filter_beschraenkt_auf_die_Organisation_aus_der_Adresse()
    {
        var (a, b) = await ZweiOrganisationenMitFreizeitAsync();

        await using var kontext = datenbank.Kontext(new MandantKontext
        {
            AktuellerMandant = b,
            SichtbareOrganisationen = [a, b],
        });

        var sichtbar = await kontext.Freizeiten.Select(f => f.TenantId).ToListAsync();

        Assert.Equal([b], sichtbar.Distinct());
    }

    /// <summary>
    /// Gegenprobe: Ohne SET ROLE sieht der Wartungszugang beide Organisationen.
    /// Ohne diesen Test könnte ein stillschweigend wirkungsloser Interceptor als
    /// funktionierende Trennung durchgehen — beide Fälle sähen gleich aus.
    /// </summary>
    [Fact]
    public async Task Wartungszugang_sieht_alle_Organisationen()
    {
        var (a, b) = await ZweiOrganisationenMitFreizeitAsync();

        await using var wartung = datenbank.Kontext(new MandantKontext { Wartung = true });

        var sichtbar = await wartung.Freizeiten
            .IgnoreQueryFilters()
            .Select(f => f.TenantId)
            .ToListAsync();

        Assert.Contains(a, sichtbar);
        Assert.Contains(b, sichtbar);
    }

    private async Task<(Guid A, Guid B)> ZweiOrganisationenMitFreizeitAsync()
    {
        await using var wartung = datenbank.Kontext(new MandantKontext { Wartung = true });

        var a = await OrganisationAnlegenAsync(wartung, "A");
        var b = await OrganisationAnlegenAsync(wartung, "B");

        return (a, b);
    }

    private static async Task<Guid> OrganisationAnlegenAsync(ZeltlotseDbContext kontext, string kennung)
    {
        var id = Guid.NewGuid();

        kontext.Organisationen.Add(new Organisation
        {
            Id = id,
            Name = $"Organisation {kennung} {id:N}",
            Slug = $"org-{kennung.ToLowerInvariant()}-{id:N}",
            ErstelltAm = DateTimeOffset.UtcNow,
        });

        kontext.Freizeiten.Add(new Freizeit
        {
            Id = Guid.NewGuid(),
            TenantId = id,
            Name = $"Freizeit {kennung}",
            Status = FreizeitStatus.Offen,
            ErstelltAm = DateTimeOffset.UtcNow,
        });

        await kontext.SaveChangesAsync();

        return id;
    }
}
