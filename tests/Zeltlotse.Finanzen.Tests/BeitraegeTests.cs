using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Finanzen.Beitraege;
using Zeltlotse.Finanzen.Daten;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Rechte;
using Zeltlotse.Testing;

namespace Zeltlotse.Finanzen.Tests;

/// <summary>
/// Story 5-3, Beitrag bezahlt abhaken (CAP-34, FR-60, AD-6, AD-17, AD-22): die Merkmale der Commands (Modul Finanzen, kein <c>[ErlaubtNachAbschluss]</c>),
/// <c>ToString()</c> ohne Personenkennung und der Mandantenfilter für <see cref="BeitragVermerk"/>.
/// </summary>
public sealed class BeitraegeTests(PostgresContainer postgres)
{
    private static readonly Guid Mia = Guid.Parse("0199f800-0000-7000-8000-00000000001a");
    private static readonly Guid Lea = Guid.Parse("0199f800-0000-7000-8000-00000000001b");

    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    [Fact]
    public void Der_Uebergang_ist_ein_Benutzer_Command_des_Moduls_Finanzen_und_gilt_nicht_nach_dem_Abschluss()
    {
        var commands = typeof(BeitragUebergangCommand).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(BeitragUebergangCommand).Namespace && typeof(IBenutzerCommand).IsAssignableFrom(t) && !t.IsInterface)
            .ToList();

        Assert.Equal(["BeitragUebergangCommand"], commands.Select(t => t.Name));
        Assert.All(commands, t =>
        {
            Assert.True(typeof(ISchalterCommand).IsAssignableFrom(t), t.Name);
            Assert.True(typeof(IModulCommand<FinanzenDbContext>).IsAssignableFrom(t), t.Name);
            Assert.False(t.IsDefined(typeof(ErlaubtNachAbschlussAttribute), false), t.Name);
        });
        Assert.Equal(FunktionsSchalter.Finanzen, new BeitragUebergangCommand(Guid.CreateVersion7(), Mia, Lea, BeitragUebergang.Bezahlt).Schalter);
    }

    [Fact]
    public void Der_Command_gibt_keine_Personenkennung_aus()
    {
        foreach (var uebergang in Enum.GetValues<BeitragUebergang>())
        {
            var text = new BeitragUebergangCommand(Guid.CreateVersion7(), Mia, Lea, uebergang).ToString();
            Assert.DoesNotContain(Mia.ToString(), text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Lea.ToString(), text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Die_Bedingung_des_eindeutigen_Index_nennt_den_Platzhalter() =>
        Assert.Equal(FinanzenModell.OhnePlatzhalter, $"\"PersonId\" <> '{GeloeschtePerson.Id}'");

    [Fact]
    public async Task Mandantenfilter_der_Lese_Kontext_liefert_je_Mandant_nur_die_eigenen_Vermerke()
    {
        await using var factory = new ZeltlotseApiFactory(await postgres.NeueMigrierteDatenbankAsync());
        _ = factory.CreateClient();
        var (orgA, orgB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var a = await VermerkAsync(factory, orgA);
        var b = await VermerkAsync(factory, orgB);

        foreach (var (org, erwartet) in new[] { (orgA, a), (orgB, b) })
        {
            Assert.Equal([erwartet], await LesenAsync(factory, org, db => db.BeitragVermerke.Select(x => x.Id).ToListAsync(Abbruch)));
        }

        Assert.Empty(await LesenAsync(factory, null, db => db.BeitragVermerke.ToListAsync(Abbruch)));

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(orgB);
        var vermerk = Assert.Single(await scope.ServiceProvider.GetRequiredService<FinanzenDbContext>().BeitragVermerke.ToListAsync(Abbruch));
        Assert.Equal((b, Mia, Lea), (vermerk.Id, vermerk.PersonId, vermerk.AbgehaktVon));
    }

    /// <summary>Ein Vermerk (Mia, abgehakt von Lea) in einer neuen Freizeit des Mandanten.</summary>
    private static async Task<Guid> VermerkAsync(ZeltlotseApiFactory factory, Guid organisation)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        var db = scope.ServiceProvider.GetRequiredService<FinanzenDbContext>();
        var vermerk = BeitragVermerk.Anlegen(Guid.CreateVersion7(), organisation, Guid.CreateVersion7(), Mia, Lea, DateTimeOffset.UtcNow);
        db.BeitragVermerke.Add(vermerk);
        await db.SaveChangesAsync(Abbruch);
        return vermerk.Id;
    }

    private static async Task<T> LesenAsync<T>(ZeltlotseApiFactory factory, Guid? organisation, Func<FinanzenLeseDbContext, Task<T>> abfrage)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        return await abfrage(scope.ServiceProvider.GetRequiredService<FinanzenLeseDbContext>());
    }
}
