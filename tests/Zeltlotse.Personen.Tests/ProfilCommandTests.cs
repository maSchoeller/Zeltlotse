using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Personen.Daten;
using Zeltlotse.Personen.Profil;
using Zeltlotse.Testing;

namespace Zeltlotse.Personen.Tests;

/// <summary>
/// Commands des Profils gegen PostgreSQL (AD-14, AD-19, AD-27): <c>ProfilSicherstellen</c> ist idempotent und legt den
/// Personen-Schlüssel in derselben Transaktion an; <c>ProfilFeldAendern</c> folgt der Konfliktregel.
/// </summary>
public sealed class ProfilCommandTests(PostgresContainer postgres) : IAsyncLifetime
{
    private static readonly Guid PersonA = Guid.Parse("0199b000-0000-7000-8000-00000000000a");

    private ZeltlotseApiFactory _factory = null!;
    private string _verbindung = null!;

    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _verbindung = await postgres.NeueDatenbankAsync();
        _factory = new ZeltlotseApiFactory(_verbindung);
        _ = _factory.CreateClient();
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Sicherstellen_legt_Person_und_Schluessel_einmal_an()
    {
        var erste = await SendenAsync(new ProfilSicherstellen(PersonA));
        var zweite = await SendenAsync(new ProfilSicherstellen(PersonA));

        Assert.Equal(PersonA, erste.Id);
        Assert.True(erste.Version > 0);
        Assert.Equal(erste.Version, zweite.Version);
        Assert.Equal(1, await LesenAsync(db => db.Personen.CountAsync(Abbruch)));
        Assert.Equal(1, await LesenAsync(db => db.PersonenSchluessel.CountAsync(s => s.PersonId == PersonA, Abbruch)));
        Assert.Equal(_factory.Uhr.GetUtcNow(), await LesenAsync(db => db.Personen.Select(p => p.AngelegtAm).SingleAsync(Abbruch)));
    }

    [Fact]
    public async Task Scheitert_das_Anlegen_der_Person_gibt_es_auch_keinen_Schluessel()
    {
        await AusfuehrenAsync("ALTER TABLE personen.person ADD CONSTRAINT keine_person CHECK (false) NOT VALID");

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => SendenAsync(new ProfilSicherstellen(PersonA)));

        Assert.Equal(0, await LesenAsync(db => db.PersonenSchluessel.CountAsync(Abbruch)));
        Assert.Equal(0, await LesenAsync(db => db.Personen.CountAsync(Abbruch)));
    }

    [Fact]
    public async Task Feld_aendern_mit_passendem_alten_Wert_speichert_und_zaehlt_die_Version_weiter()
    {
        var person = await SendenAsync(new ProfilSicherstellen(PersonA));

        var ergebnis = await SendenAsync(new ProfilFeldAendern(PersonA, ProfilFeld.Vorname, person.Version, null, " Paul "));

        var gespeichert = Assert.IsType<ProfilAenderung.Gespeichert>(ergebnis);
        Assert.Equal("Paul", gespeichert.Person.Vorname);
        Assert.NotEqual(person.Version, gespeichert.Person.Version);
        Assert.Equal(gespeichert.Person.Version, await LesenAsync(db => db.Personen.Select(p => p.Version).SingleAsync(Abbruch)));
    }

    [Fact]
    public async Task Abweichender_alter_Wert_ist_ein_Konflikt_anderes_Feld_nicht()
    {
        var person = await SendenAsync(new ProfilSicherstellen(PersonA));
        await SendenAsync(new ProfilFeldAendern(PersonA, ProfilFeld.Vorname, person.Version, null, "Paul"));

        var konflikt = await SendenAsync(new ProfilFeldAendern(PersonA, ProfilFeld.Vorname, person.Version, null, "Emma"));
        var anderes = await SendenAsync(new ProfilFeldAendern(PersonA, ProfilFeld.Nachname, person.Version, null, "Muster"));

        Assert.IsType<ProfilAenderung.Konflikt>(konflikt);
        Assert.IsType<ProfilAenderung.Gespeichert>(anderes);
        var gespeichert = await LesenAsync(db => db.Personen.SingleAsync(Abbruch));
        Assert.Equal(("Paul", "Muster"), (gespeichert.Vorname, gespeichert.Nachname));
    }

    [Fact]
    public async Task Ohne_Person_meldet_das_Aendern_PersonFehlt()
    {
        Assert.IsType<ProfilAenderung.PersonFehlt>(await SendenAsync(new ProfilFeldAendern(PersonA, ProfilFeld.Vorname, 1, null, "Paul")));
    }

    [Theory]
    [InlineData(null, "", true)]
    [InlineData("", null, true)]
    [InlineData("  ", null, true)]
    [InlineData("Paul", " Paul ", true)]
    [InlineData("Paul", "Emma", false)]
    [InlineData(null, "Emma", false)]
    public void Leere_Werte_gelten_als_nicht_angegeben_beim_Vergleich(string? aktuell, string? alterWert, bool angewendet)
    {
        var person = Person.Anlegen(PersonA, DateTimeOffset.UnixEpoch);
        person.FeldAendern(ProfilFeld.Vorname, null, aktuell);

        Assert.Equal(angewendet, person.FeldAendern(ProfilFeld.Vorname, alterWert, "Neu"));
        Assert.Equal(angewendet ? "Neu" : Person.Normalisieren(aktuell), person.Vorname);
    }

    [Fact]
    public async Task Steckbriefe_liefern_Vorname_Nachname_und_Herkunft_fuer_die_Menge_unbekannte_fehlen()
    {
        var personB = Guid.Parse("0199b000-0000-7000-8000-00000000000b");
        var a = await SendenAsync(new ProfilSicherstellen(PersonA));
        await SendenAsync(new ProfilSicherstellen(personB));
        var version = a.Version;
        foreach (var (feld, wert) in new[] { (ProfilFeld.Vorname, "Miriam"), (ProfilFeld.Nachname, "Zeltner"), (ProfilFeld.Herkunft, " Musterstadt ") })
        {
            version = Assert.IsType<ProfilAenderung.Gespeichert>(await SendenAsync(new ProfilFeldAendern(PersonA, feld, version, null, wert))).Person.Version;
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        var steckbriefe = await scope.ServiceProvider.GetRequiredService<Zeltlotse.Personen.Contracts.Namen.IPersonenSteckbriefe>()
            .SteckbriefeAsync([PersonA, personB, Guid.CreateVersion7()], Abbruch);

        Assert.Equal(2, steckbriefe.Count);
        Assert.Equal(new Zeltlotse.Personen.Contracts.Namen.PersonenSteckbrief("Miriam", "Zeltner", "Musterstadt"), steckbriefe[PersonA]);
        Assert.Equal(new Zeltlotse.Personen.Contracts.Namen.PersonenSteckbrief(null, null, null), steckbriefe[personB]);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<Zeltlotse.Personen.Contracts.Namen.IPersonenSteckbriefe>().SteckbriefeAsync([], Abbruch));
    }

    [Fact]
    public async Task Herkunft_ist_hoechstens_60_Zeichen_lang()
    {
        var person = await SendenAsync(new ProfilSicherstellen(PersonA));

        var fehler = await Assert.ThrowsAsync<Zeltlotse.SharedKernel.Commands.ValidierungsAusnahme>(
            () => SendenAsync(new ProfilFeldAendern(PersonA, ProfilFeld.Herkunft, person.Version, null, new string('x', 61))));
        var gespeichert = await SendenAsync(new ProfilFeldAendern(PersonA, ProfilFeld.Herkunft, person.Version, null, new string('x', 60)));

        Assert.Equal(["Höchstens 60 Zeichen."], fehler.Fehler[ProfilFelder.Herkunft]);
        Assert.IsType<ProfilAenderung.Gespeichert>(gespeichert);
    }

    private async Task<T> SendenAsync<T>(ICommand<T> command)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, Abbruch);
    }

    private async Task<T> LesenAsync<T>(Func<PersonenLeseDbContext, Task<T>> abfrage)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await abfrage(scope.ServiceProvider.GetRequiredService<PersonenLeseDbContext>());
    }

    private async Task AusfuehrenAsync(string sql)
    {
        await using var db = new NpgsqlConnection(_verbindung);
        await db.OpenAsync(Abbruch);
        await using var befehl = new NpgsqlCommand(sql, db);
        await befehl.ExecuteNonQueryAsync(Abbruch);
    }
}
