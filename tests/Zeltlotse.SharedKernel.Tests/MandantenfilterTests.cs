using Microsoft.EntityFrameworkCore;
using Npgsql;
using Zeltlotse.Testing;
using Zeltlotse.Testmodul.Notizen;

namespace Zeltlotse.SharedKernel.Tests;

/// <summary>Matrix-Zeile „Mandantenfilter“ (AD-6) und Lese-Kontext (AD-27).</summary>
public sealed class MandantenfilterTests(PostgresContainer postgres)
{
    private static readonly Guid OrgA = Guid.Parse("0199a000-0000-7000-8000-00000000000a");
    private static readonly Guid OrgB = Guid.Parse("0199a000-0000-7000-8000-00000000000b");

    [Fact]
    public async Task Filter_liefert_nur_Zeilen_der_eigenen_Organisation()
    {
        await using var umgebung = await TestUmgebung.ErstellenAsync(postgres);
        await umgebung.SendenAsync(new NotizAnlegen(OrgA, "von A"), OrgA);
        await umgebung.SendenAsync(new NotizAnlegen(OrgB, "von B"), OrgB);

        var texte = await umgebung.LesenAsync(db => db.Notizen.Select(n => n.Text).ToListAsync(), OrgA);

        Assert.Equal(["von A"], texte);
    }

    [Fact]
    public async Task Ohne_Kontext_liefert_der_Filter_nichts()
    {
        await using var umgebung = await TestUmgebung.ErstellenAsync(postgres);
        await umgebung.SendenAsync(new NotizAnlegen(OrgA, "von A"), OrgA);
        await umgebung.SendenAsync(new NotizAnlegen(OrgB, "von B"), OrgB);

        Assert.Empty(await umgebung.LesenAsync(db => db.Notizen.ToListAsync()));
        Assert.Empty(await umgebung.RohLesenAsync(db => db.Notizen.ToListAsync()));
    }

    [Fact]
    public async Task Lese_Kontext_verfolgt_keine_Aenderungen_und_SaveChanges_wirft()
    {
        await using var umgebung = await TestUmgebung.ErstellenAsync(postgres);
        await umgebung.SendenAsync(new NotizAnlegen(OrgA, "von A"), OrgA);

        await umgebung.LesenAsync(async db =>
        {
            var notiz = await db.Notizen.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Empty(db.ChangeTracker.Entries());
            Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
            return notiz;
        }, OrgA);
    }

    [Fact]
    public async Task Lese_Kontext_lehnt_ExecuteDelete_ab()
    {
        await using var umgebung = await TestUmgebung.ErstellenAsync(postgres);
        await umgebung.SendenAsync(new NotizAnlegen(OrgA, "bleibt"), OrgA);

        await umgebung.LesenAsync(
            db => Assert.ThrowsAsync<PostgresException>(() => db.Notizen.ExecuteDeleteAsync(TestContext.Current.CancellationToken)),
            OrgA);

        Assert.Equal(1, await umgebung.RohLesenAsync(db => db.Notizen.IgnoreQueryFilters().CountAsync()));
    }

    [Fact]
    public async Task Lese_Kontext_lehnt_beschreibbare_CTE_ab()
    {
        await using var umgebung = await TestUmgebung.ErstellenAsync(postgres);
        await umgebung.SendenAsync(new NotizAnlegen(OrgA, "bleibt"), OrgA);

        var fehler = await umgebung.LesenAsync(
            db => Assert.ThrowsAsync<PostgresException>(() => db.Database
                .SqlQueryRaw<int>("WITH d AS (DELETE FROM testmodul.notiz RETURNING 1) SELECT count(*)::int AS \"Value\" FROM d")
                .ToListAsync(TestContext.Current.CancellationToken)),
            OrgA);

        Assert.Equal(PostgresErrorCodes.ReadOnlySqlTransaction, fehler.SqlState);
        Assert.Equal(1, await umgebung.RohLesenAsync(db => db.Notizen.IgnoreQueryFilters().CountAsync()));
    }

    [Fact]
    public async Task Lese_Kontext_liest_auch_mit_TagWith()
    {
        await using var umgebung = await TestUmgebung.ErstellenAsync(postgres);
        await umgebung.SendenAsync(new NotizAnlegen(OrgA, "von A"), OrgA);

        var texte = await umgebung.LesenAsync(db => db.Notizen.TagWith("Abfrage mit Kommentar").Select(n => n.Text).ToListAsync(), OrgA);

        Assert.Equal(["von A"], texte);
    }
}
