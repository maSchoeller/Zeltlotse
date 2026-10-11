using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.Testing;
using Zeltlotse.Testmodul.Notizen;

namespace Zeltlotse.SharedKernel.Tests;

/// <summary>Matrix-Zeilen „Rundlauf verschlüsselt“ und „Schlüssel vernichtet“ gegen PostgreSQL (AD-14).</summary>
public sealed class BesondersRundlaufTests(PostgresContainer postgres)
{
    private static readonly Guid OrgA = Guid.Parse("0199a000-0000-7000-8000-00000000000a");
    private static readonly Guid PersonA = Guid.Parse("0199b000-0000-7000-8000-00000000000a");

    [Fact]
    public async Task In_PostgreSQL_steht_kein_Klartext_und_Lesen_plus_Entschluesseln_liefert_den_Text()
    {
        await using var umgebung = await TestUmgebung.ErstellenAsync(postgres);
        umgebung.Schluessel.Anlegen(PersonA, umgebung.Hauptschluessel);
        var id = await umgebung.SendenAsync(new NotizAnlegen(OrgA, "Teilnehmer"), OrgA);

        await umgebung.SendenAsync(new NotizVertraulichSetzen(id, PersonA, "Erdnussallergie"), OrgA);

        var roh = await RohSpalteAsync(umgebung, id);
        Assert.NotNull(roh);
        Assert.DoesNotContain("Erdnuss", Encoding.UTF8.GetString(roh), StringComparison.Ordinal);
        Assert.DoesNotContain("Erdnuss", Encoding.Unicode.GetString(roh), StringComparison.Ordinal);

        var gelesen = await umgebung.LesenAsync(db => db.Notizen.SingleAsync(n => n.Id == id), OrgA);
        await using var scope = umgebung.Dienste.CreateAsyncScope();
        var verschluesselung = scope.ServiceProvider.GetRequiredService<IVerschluesselung>();
        Assert.Equal("Erdnussallergie", await verschluesselung.EntschluesselnAsync(gelesen.Vertraulich!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Spalte_ist_bytea()
    {
        await using var umgebung = await TestUmgebung.ErstellenAsync(postgres);

        var typ = await umgebung.RohLesenAsync(db => db.Database
            .SqlQueryRaw<string>("SELECT data_type AS \"Value\" FROM information_schema.columns WHERE table_schema = 'testmodul' AND table_name = 'notiz' AND column_name = 'Vertraulich'")
            .SingleAsync(TestContext.Current.CancellationToken));

        Assert.Equal("bytea", typ);
    }

    [Fact]
    public async Task Nach_Vernichten_des_Schluessels_scheitert_das_Entschluesseln()
    {
        await using var umgebung = await TestUmgebung.ErstellenAsync(postgres);
        umgebung.Schluessel.Anlegen(PersonA, umgebung.Hauptschluessel);
        var id = await umgebung.SendenAsync(new NotizAnlegen(OrgA, "Teilnehmer"), OrgA);
        await umgebung.SendenAsync(new NotizVertraulichSetzen(id, PersonA, "Epilepsie"), OrgA);

        umgebung.Schluessel.Vernichten(PersonA);

        var gelesen = await umgebung.LesenAsync(db => db.Notizen.SingleAsync(n => n.Id == id), OrgA);
        await using var scope = umgebung.Dienste.CreateAsyncScope();
        var verschluesselung = scope.ServiceProvider.GetRequiredService<IVerschluesselung>();
        await Assert.ThrowsAsync<PersonenSchluesselFehltAusnahme>(
            () => verschluesselung.EntschluesselnAsync(gelesen.Vertraulich!, TestContext.Current.CancellationToken));
    }

    private static Task<byte[]?> RohSpalteAsync(TestUmgebung umgebung, Guid id) =>
        umgebung.RohLesenAsync(async db =>
        {
            await using var verbindung = new NpgsqlConnection(db.Database.GetConnectionString());
            await verbindung.OpenAsync(TestContext.Current.CancellationToken);
            await using var befehl = new NpgsqlCommand("SELECT \"Vertraulich\" FROM testmodul.notiz WHERE \"Id\" = @id", verbindung);
            befehl.Parameters.AddWithValue("id", id);
            return await befehl.ExecuteScalarAsync(TestContext.Current.CancellationToken) as byte[];
        });
}
