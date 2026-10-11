using Microsoft.EntityFrameworkCore;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.Testing;
using Zeltlotse.Testmodul.Empfang;
using Zeltlotse.Testmodul.Notizen;

namespace Zeltlotse.SharedKernel.Tests;

/// <summary>Matrix-Zeilen „Zustellung“, „Doppelt/älter“ und „Reihenfolge“.</summary>
public sealed class ZustellungTests(PostgresContainer postgres)
{
    private static readonly Guid OrgA = Guid.Parse("0199a000-0000-7000-8000-00000000000a");

    [Fact]
    public async Task Jeder_Empfaenger_laeuft_getrennt_mit_Mandanten_Kontext_aus_dem_Umschlag()
    {
        await using var umgebung = await TestUmgebung.ErstellenAsync(postgres);
        var id = await umgebung.SendenAsync(new NotizAnlegen(OrgA, "Feuerholz"), OrgA);

        await umgebung.AllesZustellenAsync();

        var empfaenge = await umgebung.RohLesenAsync(db => db.Empfaenge.OrderBy(e => e.Empfaenger).ToListAsync());
        Assert.Equal([nameof(AngelegtEmpfangenA), nameof(AngelegtEmpfangenB)], empfaenge.Select(e => e.Empfaenger));
        Assert.All(empfaenge, e =>
        {
            Assert.Equal(id, e.NotizId);
            Assert.Equal(OrgA, e.KontextOrganisationId);
        });

        Assert.Equal(2, await umgebung.RohLesenAsync(db => db.InboxEintraege.CountAsync()));
        Assert.True(await umgebung.RohLesenAsync(db => db.Zustellungen.AllAsync(z => z.ErledigtAm != null)));
    }

    [Fact]
    public async Task Fehler_bei_Empfaenger_A_haelt_B_nicht_auf_und_A_wird_spaeter_erneut_versucht()
    {
        await using var umgebung = await TestUmgebung.ErstellenAsync(postgres);
        umgebung.Stoerung.ScheitertEinmal(nameof(AngelegtEmpfangenA), folgenummer: 1);
        await umgebung.SendenAsync(new NotizAnlegen(OrgA, "Zeltheringe"), OrgA);

        await umgebung.ZustellenAsync();

        Assert.Equal([nameof(AngelegtEmpfangenB)], await EmpfaengerAsync(umgebung));
        var zustellungA = await umgebung.RohLesenAsync(db => db.Zustellungen.SingleAsync(z => z.Empfaenger.EndsWith(nameof(AngelegtEmpfangenA))));
        Assert.Null(zustellungA.ErledigtAm);
        Assert.Equal(1, zustellungA.Versuche);
        Assert.Equal(nameof(InvalidOperationException), zustellungA.LetzterFehler);

        // Vor Ablauf der Wartezeit passiert nichts.
        await umgebung.AllesZustellenAsync();
        Assert.Equal([nameof(AngelegtEmpfangenB)], await EmpfaengerAsync(umgebung));

        umgebung.Uhr.Advance(TimeSpan.FromMinutes(5));
        await umgebung.AllesZustellenAsync();

        Assert.Equal([nameof(AngelegtEmpfangenA), nameof(AngelegtEmpfangenB)], await EmpfaengerAsync(umgebung));
    }

    [Fact]
    public async Task Abgebrochene_Anfrage_im_Empfaenger_zaehlt_als_Fehlversuch_und_haelt_andere_nicht_auf()
    {
        await using var umgebung = await TestUmgebung.ErstellenAsync(postgres);
        umgebung.Stoerung.BrichtEinmalAb(nameof(AngelegtEmpfangenA), folgenummer: 1);
        await umgebung.SendenAsync(new NotizAnlegen(OrgA, "Zeltplane"), OrgA);

        await umgebung.ZustellenAsync();

        Assert.Equal([nameof(AngelegtEmpfangenB)], await EmpfaengerAsync(umgebung));
        var zustellungA = await umgebung.RohLesenAsync(db => db.Zustellungen.SingleAsync(z => z.Empfaenger.EndsWith(nameof(AngelegtEmpfangenA))));
        Assert.Null(zustellungA.ErledigtAm);
        Assert.Equal(1, zustellungA.Versuche);
        Assert.Equal(nameof(TaskCanceledException), zustellungA.LetzterFehler);
    }

    [Fact]
    public async Task Organisation_im_Umschlag_kommt_vom_Aggregat_nicht_vom_Kontext_des_Absenders()
    {
        await using var umgebung = await TestUmgebung.ErstellenAsync(postgres);
        var orgB = Guid.Parse("0199a000-0000-7000-8000-00000000000b");
        await umgebung.SendenAsync(new NotizAnlegen(orgB, "fremd angelegt"), OrgA);

        await umgebung.AllesZustellenAsync();

        var outbox = await umgebung.RohLesenAsync(db => db.Outbox.SingleAsync());
        Assert.Equal(orgB, outbox.OrganisationId);
        var empfaenge = await umgebung.RohLesenAsync(db => db.Empfaenge.ToListAsync());
        Assert.Equal(2, empfaenge.Count);
        Assert.All(empfaenge, e => Assert.Equal(orgB, e.KontextOrganisationId));
    }

    [Fact]
    public void Wartezeit_waechst_und_ist_auf_eine_Stunde_begrenzt()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), Outbox.OutboxZustellung.Wartezeit(1));
        Assert.Equal(TimeSpan.FromSeconds(20), Outbox.OutboxZustellung.Wartezeit(2));
        Assert.Equal(TimeSpan.FromHours(1), Outbox.OutboxZustellung.Wartezeit(50));
    }

    [Fact]
    public async Task Dasselbe_Ereignis_erneut_wird_verworfen()
    {
        await using var umgebung = await TestUmgebung.ErstellenAsync(postgres);
        var umschlag = new Umschlag(Guid.CreateVersion7(), umgebung.Uhr.GetUtcNow(), OrgA, Guid.CreateVersion7(), 1);

        await umgebung.SendenAsync(new AngelegtEmpfangenA(umschlag, umschlag.DatensatzId));
        await umgebung.SendenAsync(new AngelegtEmpfangenA(umschlag, umschlag.DatensatzId));

        Assert.Equal(1, await umgebung.RohLesenAsync(db => db.Empfaenge.CountAsync()));
    }

    [Fact]
    public async Task Aelteres_Ereignis_desselben_Datensatzes_wird_verworfen()
    {
        await using var umgebung = await TestUmgebung.ErstellenAsync(postgres);
        var datensatz = Guid.CreateVersion7();
        var neuer = new Umschlag(Guid.CreateVersion7(), umgebung.Uhr.GetUtcNow(), OrgA, datensatz, 5);
        var aelter = new Umschlag(Guid.CreateVersion7(), umgebung.Uhr.GetUtcNow(), OrgA, datensatz, 3);

        await umgebung.SendenAsync(new GeaendertEmpfangen(neuer, datensatz));
        await umgebung.SendenAsync(new GeaendertEmpfangen(aelter, datensatz));

        var empfaenge = await umgebung.RohLesenAsync(db => db.Empfaenge.ToListAsync());
        Assert.Equal([5L], empfaenge.Select(e => e.Folgenummer));
    }

    [Fact]
    public async Task Folgeereignis_eines_Datensatzes_wartet_bis_das_fruehere_zugestellt_ist()
    {
        await using var umgebung = await TestUmgebung.ErstellenAsync(postgres);
        umgebung.Stoerung.ScheitertEinmal(nameof(GeaendertEmpfangen), folgenummer: 2);
        var id = await umgebung.SendenAsync(new NotizAnlegen(OrgA, "Plan 1"), OrgA);
        await umgebung.SendenAsync(new NotizAendern(id, "Plan 2"), OrgA);
        await umgebung.SendenAsync(new NotizAendern(id, "Plan 3"), OrgA);

        await umgebung.AllesZustellenAsync();

        Assert.Empty(await FolgenummernAsync(umgebung, nameof(GeaendertEmpfangen)));

        umgebung.Uhr.Advance(TimeSpan.FromMinutes(5));
        await umgebung.AllesZustellenAsync();

        Assert.Equal([2L, 3L], await FolgenummernAsync(umgebung, nameof(GeaendertEmpfangen)));
    }

    private static Task<List<string>> EmpfaengerAsync(TestUmgebung umgebung) =>
        umgebung.RohLesenAsync(db => db.Empfaenge.OrderBy(e => e.Empfaenger).Select(e => e.Empfaenger).ToListAsync());

    private static Task<List<long>> FolgenummernAsync(TestUmgebung umgebung, string empfaenger) =>
        umgebung.RohLesenAsync(db => db.Empfaenge
            .Where(e => e.Empfaenger == empfaenger)
            .OrderBy(e => e.Folgenummer)
            .Select(e => e.Folgenummer)
            .ToListAsync());
}
