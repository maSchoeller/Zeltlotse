using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Zeltlotse.Personen.Freizeitpass;
using Zeltlotse.Personen.Nachweise;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mail;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.Testing;

namespace Zeltlotse.Personen.Tests;

/// <summary>
/// Story 2-6, Erinnern zum Freizeitpass (FR-25, AD-13) mit künstlicher Uhr: nur bei aktiver Freigabe der Person in dieser Freizeit, nicht für
/// sich selbst; eine Zeile je Freizeit und Person, höchstens einmal je Kalendertag (Europe/Berlin); Mail-Auftrag nur mit Kennungen in der
/// Outbox, keine Einsicht. Die Mail entsteht erst beim Versand; ohne Empfänger keine.
/// </summary>
public sealed class FreizeitpassErinnerungTests(PostgresContainer postgres) : NachweisTestBasis(postgres)
{
    private static readonly Guid OrgA = Guid.Parse("0199d000-0000-7000-8000-00000000a6a1");
    private static readonly Guid Zeltlager = Guid.Parse("0199d000-0000-7000-8000-0000000f6011");
    private static readonly Guid Freizeit2 = Guid.Parse("0199d000-0000-7000-8000-0000000f6012");

    private static readonly string MailTyp = typeof(FreizeitpassErinnerungMail).FullName!;

    private Task<ErinnerungErgebnis> ErinnernAsync(Guid? person, Guid von = default, Guid? freizeit = null) =>
        SendenAsync(new FreizeitpassErinnern(OrgA, freizeit ?? Zeltlager, von == default ? Miriam : von, person), OrgA);

    /// <summary>Eine Freigabe wie nach dem Eingang einer Anmeldung, gültig bis <paramref name="gueltigBis"/>.</summary>
    private async Task FreigabeAsync(Guid person, Guid freizeit, DateTimeOffset gueltigBis)
    {
        await using var db = new NpgsqlConnection(Verbindung);
        await db.OpenAsync(Abbruch);
        await using var befehl = new NpgsqlCommand(
            "INSERT INTO personen.freigabe (\"Id\", \"OrganisationId\", \"PersonId\", \"FreizeitId\", \"AnmeldungId\", \"Stufe\", \"GueltigBis\", \"AngelegtAm\", \"Fragen\") " +
            "VALUES (@id, @o, @p, @f, @a, 1, @bis, @am, ARRAY['allergien'])",
            db);
        befehl.Parameters.AddWithValue("id", Guid.CreateVersion7());
        befehl.Parameters.AddWithValue("o", OrgA);
        befehl.Parameters.AddWithValue("p", person);
        befehl.Parameters.AddWithValue("f", freizeit);
        befehl.Parameters.AddWithValue("a", Guid.CreateVersion7());
        befehl.Parameters.AddWithValue("bis", gueltigBis);
        befehl.Parameters.AddWithValue("am", Factory.Uhr.GetUtcNow());
        await befehl.ExecuteNonQueryAsync(Abbruch);
    }

    private DateTimeOffset Spaeter => Factory.Uhr.GetUtcNow().AddDays(60);

    [Fact]
    public async Task Erinnern_speichert_die_Erinnerung_merkt_genau_eine_Mail_nur_mit_Kennungen_vor_und_fragt_keine_Einsicht_an()
    {
        await FreigabeAsync(Jonas, Zeltlager, Spaeter);

        var erinnert = Assert.IsType<ErinnerungErgebnis.Erinnert>(await ErinnernAsync(Jonas));

        Assert.Equal(Factory.Uhr.GetUtcNow(), erinnert.ErinnertAm);
        var erinnerung = Assert.Single(await LesenAsync(db => db.FreizeitpassErinnerungen.ToListAsync(Abbruch), OrgA));
        Assert.Equal(
            (OrgA, Zeltlager, Jonas, Miriam, Factory.Uhr.GetUtcNow(), 1L),
            (erinnerung.OrganisationId, erinnerung.FreizeitId, erinnerung.PersonId, erinnerung.ErinnertVon, erinnerung.ErinnertAm, erinnerung.Folgenummer));
        Assert.Empty(await LesenAsync(db => db.Erinnerungen.ToListAsync(Abbruch), OrgA));
        Assert.Equal(0L, await WertAsync<long>("SELECT count(*) FROM personen.nachweis_einsicht"));

        var inhalt = await WertAsync<string>($"SELECT \"Inhalt\"::text FROM personen.outbox WHERE \"Typ\" = '{MailTyp}'");
        using var json = JsonDocument.Parse(inhalt);
        Assert.Equal([("personId", Jonas.ToString()), ("erinnertVon", Miriam.ToString())], json.RootElement.EnumerateObject().Select(p => (p.Name, p.Value.GetString())));
        Assert.Equal(MailAuftraege.Empfaenger, await WertAsync<string>(
            $"SELECT z.\"Empfaenger\" FROM personen.outbox_zustellung z JOIN personen.outbox o ON o.\"Id\" = z.\"EreignisId\" WHERE o.\"Typ\" = '{MailTyp}'"));
        Assert.Equal(1L, await WertAsync<long>("SELECT count(*) FROM personen.outbox_zustellung WHERE \"Empfaenger\" = 'Zeltlotse.Mail'"));
    }

    [Fact]
    public async Task Hoechstens_einmal_je_Kalendertag_in_Berlin_und_eine_Zeile_je_Freizeit_und_Person()
    {
        await FreigabeAsync(Jonas, Zeltlager, Spaeter.AddDays(30));
        await FreigabeAsync(Jonas, Freizeit2, Spaeter.AddDays(30));
        Assert.IsType<ErinnerungErgebnis.Erinnert>(await ErinnernAsync(Jonas));
        // Dieselbe Person in einer anderen Freizeit: eine eigene Zeile, am selben Tag erlaubt.
        Assert.IsType<ErinnerungErgebnis.Erinnert>(await ErinnernAsync(Jonas, freizeit: Freizeit2));

        // 08:00 UTC + 13:59 = 21:59 UTC = 23:59 MESZ: noch derselbe Tag, auch für die zweite Leitung.
        Factory.Uhr.Advance(TimeSpan.FromHours(13) + TimeSpan.FromMinutes(59));
        Assert.IsType<ErinnerungErgebnis.SchonHeute>(await ErinnernAsync(Jonas));
        Assert.IsType<ErinnerungErgebnis.SchonHeute>(await ErinnernAsync(Jonas, Tobias));

        Factory.Uhr.Advance(TimeSpan.FromMinutes(1));
        var zweite = Assert.IsType<ErinnerungErgebnis.Erinnert>(await ErinnernAsync(Jonas, Tobias));

        var erinnerung = Assert.Single(await LesenAsync(db => db.FreizeitpassErinnerungen.Where(e => e.FreizeitId == Zeltlager).ToListAsync(Abbruch), OrgA));
        Assert.Equal((Tobias, zweite.ErinnertAm, 2L), (erinnerung.ErinnertVon, erinnerung.ErinnertAm, erinnerung.Folgenummer));
        Assert.Equal(2, await LesenAsync(db => db.FreizeitpassErinnerungen.CountAsync(Abbruch), OrgA));
        Assert.Equal(3L, await WertAsync<long>($"SELECT count(*) FROM personen.outbox WHERE \"Typ\" = '{MailTyp}'"));
    }

    [Fact]
    public async Task Nicht_fuer_sich_selbst_nicht_ohne_aktive_Freigabe_in_dieser_Freizeit_und_ohne_Person_ein_Feldfehler()
    {
        await FreigabeAsync(Miriam, Zeltlager, Spaeter);
        await FreigabeAsync(Lea, Freizeit2, Spaeter);
        await FreigabeAsync(Tobias, Zeltlager, Factory.Uhr.GetUtcNow().AddMinutes(-1));

        Assert.IsType<ErinnerungErgebnis.NichtGefunden>(await ErinnernAsync(Miriam));
        Assert.IsType<ErinnerungErgebnis.NichtGefunden>(await ErinnernAsync(Lea));
        Assert.IsType<ErinnerungErgebnis.NichtGefunden>(await ErinnernAsync(Tobias));
        Assert.IsType<ErinnerungErgebnis.NichtGefunden>(await ErinnernAsync(Guid.CreateVersion7()));
        var fehler = await Assert.ThrowsAsync<ValidierungsAusnahme>(() => ErinnernAsync(null));
        Assert.Equal(["Person fehlt."], fehler.Fehler["personId"]);

        Assert.Empty(await LesenAsync(db => db.FreizeitpassErinnerungen.ToListAsync(Abbruch), OrgA));
        Assert.Equal(0L, await WertAsync<long>("SELECT count(*) FROM personen.outbox_zustellung WHERE \"Empfaenger\" = 'Zeltlotse.Mail'"));
    }

    [Fact]
    public async Task Ohne_Empfaenger_baut_die_Erinnerung_keine_Mail()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(OrgA);
        var aufbau = scope.ServiceProvider.GetRequiredService<IMailAufbau<FreizeitpassErinnerungMail>>();

        // Die Profile der Tests haben kein Konto, keine Familie und keinen Gast-Zugang.
        Assert.Null(await aufbau.AufbauenAsync(new FreizeitpassErinnerungMail(Jonas, Miriam), Abbruch));
    }

    [Fact]
    public void Text_nennt_Leitung_Person_und_Link_ohne_Anrede_ohne_Fragen_und_ohne_Antworten()
    {
        var text = FreizeitpassErinnerungsMail.Text("https://zeltlotse.example.org", "Miriam", "Zeltner", "Mia", "Muster");

        Assert.Equal("Erinnerung: Freizeitpass in Zeltlotse", FreizeitpassErinnerungsMail.Betreff);
        Assert.StartsWith(
            "Miriam Zeltner aus der Leitung einer Freizeit bittet darum, den Freizeitpass von Mia Muster in Zeltlotse zu ergänzen. Mit Konto steht der Freizeitpass unter „Familie“ bei der Anmeldung:",
            text,
            StringComparison.Ordinal);
        Assert.Contains("\nhttps://zeltlotse.example.org/app/familie\n", text.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("Ohne Konto öffnet der Gast-Link aus der Mail zur Anmeldung den Freizeitpass.", text, StringComparison.Ordinal);
        Assert.EndsWith("Diese Mail hat die Leitung ausgelöst.", text.TrimEnd(), StringComparison.Ordinal);
        Assert.DoesNotContain("Allerg", text, StringComparison.Ordinal);

        var ohneNamen = FreizeitpassErinnerungsMail.Text("https://zeltlotse.example.org", null, " ", null, null);
        Assert.StartsWith("Eine Person aus der Leitung einer Freizeit bittet darum, den Freizeitpass von Eine Person in Zeltlotse zu ergänzen.", ohneNamen, StringComparison.Ordinal);
    }
}
