using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Personen.Contracts.Freizeitpass;
using Zeltlotse.Personen.Freizeitpass;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.Testing;

namespace Zeltlotse.Personen.Tests;

/// <summary>
/// Story 4-4, Lesepfade ohne Menschen für die Küche (AD-8, AD-10, FR-48, FR-52) mit künstlicher Uhr: „Sonderkost für die Küche“ liest nur die Auswahl der
/// Frage „Ernährung“ (nie „Sonstiges“) von Personen mit gültiger Freigabe der Stufe Team, deren Fragen „Ernährung“ enthalten, und protokolliert je
/// gelesener Person höchstens einmal am Tag den Zweck „Sonderkost-Abgleich der Küche“ ohne lesende Person. Der Allergen-Pfad teilt den Kern und bleibt,
/// wie er ist.
/// </summary>
public sealed class KuechenProfileTests(PostgresContainer postgres) : NachweisTestBasis(postgres)
{
    private static readonly Guid OrgA = Guid.Parse("0199d000-0000-7000-8000-00000000a4a1");
    private static readonly Guid Zeltlager = Guid.Parse("0199d000-0000-7000-8000-0000000f4011");

    private DateTimeOffset Spaeter => Factory.Uhr.GetUtcNow().AddDays(60);

    /// <summary>Eine Freigabe der Person für das Zeltlager (Stufe 1 Leitung, 2 Team) mit diesen Fragen, gültig bis <paramref name="gueltigBis"/>.</summary>
    private async Task FreigabeAsync(Guid person, short stufe, string[] fragen, DateTimeOffset gueltigBis)
    {
        await using var db = new NpgsqlConnection(Verbindung);
        await db.OpenAsync(Abbruch);
        await using var befehl = new NpgsqlCommand(
            "INSERT INTO personen.freigabe (\"Id\", \"OrganisationId\", \"PersonId\", \"FreizeitId\", \"AnmeldungId\", \"Stufe\", \"GueltigBis\", \"AngelegtAm\", \"Fragen\") " +
            "VALUES (@id, @o, @p, @f, @a, @s, @bis, @am, @fragen)",
            db);
        befehl.Parameters.AddWithValue("id", Guid.CreateVersion7());
        befehl.Parameters.AddWithValue("o", OrgA);
        befehl.Parameters.AddWithValue("p", person);
        befehl.Parameters.AddWithValue("f", Zeltlager);
        befehl.Parameters.AddWithValue("a", Guid.CreateVersion7());
        befehl.Parameters.AddWithValue("s", stufe);
        befehl.Parameters.AddWithValue("bis", gueltigBis);
        befehl.Parameters.AddWithValue("am", Factory.Uhr.GetUtcNow());
        befehl.Parameters.AddWithValue("fragen", fragen);
        await befehl.ExecuteNonQueryAsync(Abbruch);
    }

    /// <summary>Die Person beantwortet ihren eigenen Freizeitpass.</summary>
    private async Task AntwortAsync(Guid person, string kennung, string wert, FreizeitpassFeld feld = FreizeitpassFeld.Wert) =>
        Assert.IsType<AntwortAenderung.Gespeichert>(await SendenAsync(new FreizeitpassAntwortAendern(person, person, kennung, feld, null, wert)));

    private async Task<IReadOnlyList<IReadOnlyList<string>>> SonderkostAsync(params Guid[] personen)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(OrgA);
        return await scope.ServiceProvider.GetRequiredService<IFreizeitpassSonderkost>().ProfileAsync(Zeltlager, personen, Abbruch);
    }

    private async Task<IReadOnlyList<IReadOnlyList<string>>> AllergeneAsync(params Guid[] personen)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(OrgA);
        return await scope.ServiceProvider.GetRequiredService<IFreizeitpassAllergene>().ProfileAsync(Zeltlager, personen, Abbruch);
    }

    private Task<long> ProtokollAsync(Guid person, int zweck) => WertAsync<long>(
        $"SELECT count(*) FROM personen.einsicht_protokoll WHERE \"BetroffenePersonId\" = '{person}' AND \"Zweck\" = {zweck} AND \"LesendePersonId\" IS NULL");

    [Fact]
    public async Task Sonderkost_liest_nur_die_Auswahl_bestaetigter_Personen_mit_gueltiger_Freigabe_und_der_Frage_Ernaehrung()
    {
        // Jonas: Team, Frage freigegeben, vegetarisch und „Sonstiges“. Lea: Team, vegan + kein Schweinefleisch. Miriam: nur Stufe Leitung (eingegangen).
        // Tobias: Team, aber abgelaufen. Andrea: Team, aber nur die Allergie-Frage freigegeben.
        await FreigabeAsync(Jonas, 2, ["allergien", "ernaehrung"], Spaeter);
        await FreigabeAsync(Lea, 2, ["ernaehrung"], Spaeter);
        await FreigabeAsync(Miriam, 1, ["ernaehrung"], Spaeter);
        await FreigabeAsync(Tobias, 2, ["ernaehrung"], Factory.Uhr.GetUtcNow().AddMinutes(-1));
        await FreigabeAsync(Andrea, 2, ["allergien"], Spaeter);
        await AntwortAsync(Jonas, Fragenkatalog.Ernaehrung, "vegetarisch");
        await AntwortAsync(Jonas, Fragenkatalog.Ernaehrung, "Keine Pilze", FreizeitpassFeld.Sonstiges);
        await AntwortAsync(Lea, Fragenkatalog.Ernaehrung, "kein-schweinefleisch,vegan");
        foreach (var person in new[] { Miriam, Tobias, Andrea })
        {
            await AntwortAsync(person, Fragenkatalog.Ernaehrung, "vegan");
        }

        var profile = await SonderkostAsync(Jonas, Lea, Miriam, Tobias, Andrea, Guid.CreateVersion7());

        // Nur die Auswahl in Katalogreihenfolge, sortiert, ohne „Sonstiges“ und ohne Kennungen.
        Assert.Equal([["vegan", "kein-schweinefleisch"], ["vegetarisch"]], profile.Select(p => p.ToArray()));
        Assert.DoesNotContain(profile.SelectMany(p => p), s => s.Contains("Pilze", StringComparison.Ordinal));
        Assert.Equal((1L, 1L), (await ProtokollAsync(Jonas, 4), await ProtokollAsync(Lea, 4)));
        foreach (var person in new[] { Miriam, Tobias, Andrea })
        {
            Assert.Equal(0L, await ProtokollAsync(person, 4));
        }
    }

    [Fact]
    public async Task Sonderkost_Protokoll_einmal_am_Tag_ohne_lesende_Person_am_naechsten_Tag_wieder()
    {
        await FreigabeAsync(Jonas, 2, ["ernaehrung"], Spaeter);
        await AntwortAsync(Jonas, Fragenkatalog.Ernaehrung, "vegetarisch");

        await SonderkostAsync(Jonas);
        await SonderkostAsync(Jonas);

        Assert.Equal(1L, await ProtokollAsync(Jonas, 4));
        Assert.Equal(0L, await ProtokollAsync(Jonas, 3));
        Assert.Equal(1L, await WertAsync<long>($"SELECT count(*) FROM personen.einsicht_protokoll WHERE \"BetroffenePersonId\" = '{Jonas}'"));

        Factory.Uhr.Advance(TimeSpan.FromDays(1));
        await SonderkostAsync(Jonas);
        Assert.Equal(2L, await ProtokollAsync(Jonas, 4));
    }

    [Fact]
    public async Task Ohne_Personen_oder_ohne_Antwort_leer_und_kein_Protokoll()
    {
        await FreigabeAsync(Jonas, 2, ["ernaehrung"], Spaeter);

        Assert.Empty(await SonderkostAsync());
        Assert.Empty(await SonderkostAsync(Jonas));
        Assert.Equal(0L, await ProtokollAsync(Jonas, 4));
    }

    [Fact]
    public async Task Allergen_Pfad_bleibt_eigener_Zweck_und_eigene_Frage()
    {
        await FreigabeAsync(Jonas, 2, ["allergien", "ernaehrung"], Spaeter);
        await AntwortAsync(Jonas, Fragenkatalog.Allergien, "milch,gluten");
        await AntwortAsync(Jonas, Fragenkatalog.Allergien, "Kiwi", FreizeitpassFeld.Sonstiges);
        await AntwortAsync(Jonas, Fragenkatalog.Ernaehrung, "vegan");

        var allergene = await AllergeneAsync(Jonas);
        var sonderkost = await SonderkostAsync(Jonas);

        Assert.Equal([["gluten", "milch"]], allergene.Select(p => p.ToArray()));
        Assert.Equal([["vegan"]], sonderkost.Select(p => p.ToArray()));
        Assert.Equal((1L, 1L), (await ProtokollAsync(Jonas, 3), await ProtokollAsync(Jonas, 4)));
    }
}
