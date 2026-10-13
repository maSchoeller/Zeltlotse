using System.Net;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.FreizeitpassSzenario;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Story 2-5, Personenliste der Freizeit (FR-24, AD-2, AD-30) gegen echtes PostgreSQL: <c>GET /api/freizeiten/{freizeitId}/personenliste</c>
/// für Leitung und Team mit eingegangenen und bestätigten Anmeldungen (ohne abgesagte), sortiert nach Art, Nachname, Vorname; ohne
/// Rolle, fremde Organisation und Global Admin <c>404</c>.
/// </summary>
public sealed class PersonenlisteTests(PostgresContainer postgres) : IAsyncLifetime
{
    private FreizeitpassSzenario _s = null!;

    public async ValueTask InitializeAsync() => _s = await AufbauenAsync(postgres);

    public async ValueTask DisposeAsync() => await _s.DisposeAsync();

    [Fact]
    public async Task Leitung_und_Team_sehen_die_Personen_nach_Art_Nachname_Vorname_ohne_Abgesagte()
    {
        var mia = await _s.KindAnmeldenAsync(_s.Mia);
        var emma = await _s.KindAnmeldenAsync(_s.Emma);
        await _s.BestaetigenAsync(mia);
        // Eine Anmeldung in Z gehört nicht hierher.
        await _s.KindAnmeldenAsync(_s.Emma, _s.TeilnehmendeZ);

        foreach (var client in new[] { _s.Miriam, _s.Tobias })
        {
            using var antwort = await client.GetAsync(PersonenlistePfad(_s.X), Abbruch);

            Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
            var zeilen = (await JsonAsync(antwort)).EnumerateArray().ToList();
            Assert.All(zeilen, z => Assert.Equal(["anmeldungId", "art", "nachname", "personId", "status", "vorname"], Namen(z)));
            Assert.Equal(
                ["Emma Muster Teilnehmende Eingegangen", "Mia Muster Teilnehmende Bestaetigt", "Tobias Wanderer Mitarbeitende Bestaetigt"],
                zeilen.Select(z => $"{z.GetProperty("vorname").GetString()} {z.GetProperty("nachname").GetString()} {z.GetProperty("art").GetString()} {z.GetProperty("status").GetString()}"));
            Assert.Equal([emma, mia, _s.AnmeldungTobias], zeilen.Select(z => z.GetProperty("anmeldungId").GetGuid()));
            Assert.Equal([_s.Emma, _s.Mia, _s.TobiasId], zeilen.Select(z => z.GetProperty("personId").GetGuid()));
        }

        await _s.AbsagenAsync(emma);
        var nachAbsage = (await JsonAsync(await _s.Miriam.GetAsync(PersonenlistePfad(_s.X), Abbruch))).EnumerateArray().ToList();
        Assert.Equal([mia, _s.AnmeldungTobias], nachAbsage.Select(z => z.GetProperty("anmeldungId").GetGuid()));
    }

    [Fact]
    public async Task Sortiert_wird_nach_Nachname_vor_Vorname()
    {
        // Anna steht nach Vorname vorn, nach Nachname (Zimmer) aber hinter Emma und Mia Muster.
        var anna = await FamilienHelfer.KindAsync(_s.Katrin, "Anna", new DateOnly(2015, 3, 1), "Zimmer");
        await _s.KindAnmeldenAsync(anna);
        await _s.KindAnmeldenAsync(_s.Mia);
        await _s.KindAnmeldenAsync(_s.Emma);

        var zeilen = (await JsonAsync(await _s.Miriam.GetAsync(PersonenlistePfad(_s.X), Abbruch))).EnumerateArray().ToList();

        Assert.Equal(
            ["Emma Muster", "Mia Muster", "Anna Zimmer", "Tobias Wanderer"],
            zeilen.Select(z => $"{z.GetProperty("vorname").GetString()} {z.GetProperty("nachname").GetString()}"));
    }

    [Fact]
    public async Task Eine_geloeschte_Person_heisst_Geloeschte_Person()
    {
        var emma = await _s.KindAnmeldenAsync(_s.Emma);
        await AusfuehrenAsync(_s.Verbindung, "UPDATE freizeiten.anmeldung SET \"PersonId\" = @p WHERE \"Id\" = @a", ("p", GeloeschtePerson.Id), ("a", emma));

        var zeile = (await JsonAsync(await _s.Miriam.GetAsync(PersonenlistePfad(_s.X), Abbruch))).EnumerateArray()
            .Single(z => z.GetProperty("anmeldungId").GetGuid() == emma);

        Assert.Equal(("Gelöschte Person", null), (zeile.GetProperty("vorname").GetString(), zeile.GetProperty("nachname").GetString()));
    }

    [Fact]
    public async Task Org_Admin_ohne_Rolle_fremde_Organisation_Global_Admin_und_unbekannte_Freizeit_ergeben_404()
    {
        await _s.KindAnmeldenAsync(_s.Emma);

        foreach (var (client, freizeit) in new[] { (_s.Andrea, _s.X), (_s.Lea, _s.X), (_s.Gerd, _s.X), (_s.Katrin, _s.X), (_s.Miriam, Guid.CreateVersion7()) })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(PersonenlistePfad(freizeit), Abbruch)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await _s.Anonym.GetAsync(PersonenlistePfad(_s.X), Abbruch)).StatusCode);
    }
}
