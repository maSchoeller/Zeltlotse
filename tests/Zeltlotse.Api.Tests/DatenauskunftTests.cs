using System.Net;
using System.Net.Http.Json;
using Zeltlotse.Api.Contracts.Betrieb;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;
using Zeltlotse.Testing;
using static Zeltlotse.Api.Tests.AnmeldeHelfer;
using static Zeltlotse.Api.Tests.NachweisSzenario;
using static Zeltlotse.Api.Tests.TestHelfer;

namespace Zeltlotse.Api.Tests;

/// <summary>
/// Datenauskunft <c>GET /api/ich/daten</c> (FR-14, AD-16, AD-25, Story 1-7): mit frischer Anmeldung alle Abschnitte der eigenen
/// Person aus allen Modulen, ohne fremde Daten, protokolliert als <c>Export</c>; mit alter Anmeldung oder ohne Sitzung <c>401</c>.
/// </summary>
public sealed class DatenauskunftTests(PostgresContainer postgres, S3Container s3)
{
    private const string Pfad = "/api/ich/daten";

    [Fact]
    public async Task Frische_Anmeldung_liefert_die_Abschnitte_aller_Module_ohne_fremde_Daten_und_protokolliert_den_Export()
    {
        await using var s = await ErstellenAsync(postgres, s3);
        var (einsicht, _, eh, _) = await s.FreigegebenUndUebernommenAsync();
        (await BestaetigenAsync(s.MiriamClient, s.FreizeitPfad, einsicht, eh)).Dispose();
        await DetailAsync(s.MiriamClient, s.FreizeitPfad, einsicht);
        await s.Factory.OutboxJetztZustellenAsync(Abbruch);
        // Ein fremder Nachweis (Miriam) darf in Jonas' Auskunft nicht auftauchen.
        await NachweisAsync(s.MiriamClient, "praeventionsschulung", "2024-01-10");
        await OrgHelfer.EinladenAsync(s.AndreaClient, s.OrgId, Jonas, Zeltlotse.Api.Contracts.Gemeinsam.OrgRolle.OrgUeberblick);
        (await OrgHelfer.AnnehmenAsync(s.JonasClient, OrgHelfer.Token(s.Factory, Jonas))).Dispose();
        await AnmeldenAsync(s.Factory, s.JonasClient, Jonas);
        s.Factory.Uhr.Advance(TimeSpan.FromMinutes(2));
        var jonas = await s.PersonIdAsync(Jonas);
        var vorher = await ProtokollAsync(s.Verbindung, SicherheitsEreignisArt.Export, jonas);

        using var antwort = await s.JonasClient.GetAsync(Pfad, Abbruch);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var text = await antwort.Content.ReadAsStringAsync(Abbruch);
        Assert.Equal(["abschnitte", "erstelltAm"], Namen(System.Text.Json.JsonDocument.Parse(text).RootElement));
        var auskunft = System.Text.Json.JsonSerializer.Deserialize<DatenauskunftAntwort>(text, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.Equal(s.Factory.Uhr.GetUtcNow(), auskunft.ErstelltAm);
        Assert.Equal(
            ["Konto", "Profil", "Nachweise", "Einsicht-Anfragen", "Zugriffsprotokoll", "Rollen in Organisationen", "Nachweis-Bestätigungen", "Mitarbeit in Freizeiten", "Sicherheitsprotokoll"],
            auskunft.Abschnitte.Select(a => a.Titel));
        Assert.All(auskunft.Abschnitte, a => Assert.NotEmpty(a.Zeilen));
        Assert.Contains(new DatenauskunftZeile("Mail-Adresse", Jonas), Abschnitt(auskunft, "Konto"));
        Assert.Contains(new DatenauskunftZeile("Passwort festgelegt", "nein"), Abschnitt(auskunft, "Konto"));
        Assert.Contains(new DatenauskunftZeile("Vorname", "Jonas"), Abschnitt(auskunft, "Profil"));
        Assert.Contains(new DatenauskunftZeile("Nachname", "Hüttner"), Abschnitt(auskunft, "Profil"));
        Assert.Equal(["Führungszeugnis", "JuLeiCa", "Erste Hilfe"], Abschnitt(auskunft, "Nachweise").Select(z => z.Feld));
        Assert.StartsWith("ausgestellt am 03.11.2025, hochgeladen am ", Abschnitt(auskunft, "Nachweise")[0].Wert, StringComparison.Ordinal);
        Assert.Contains("prüfende Person: Miriam Zeltner", Assert.Single(Abschnitt(auskunft, "Einsicht-Anfragen")).Wert, StringComparison.Ordinal);
        Assert.EndsWith("Miriam Zeltner: Nachweise eingesehen", Assert.Single(Abschnitt(auskunft, "Zugriffsprotokoll")).Wert, StringComparison.Ordinal);
        Assert.Equal(new DatenauskunftZeile("Jugendwerk Musterstadt", $"Org-Überblick seit {FreizeitHelfer.Heute(s.Factory):dd.MM.yyyy}"), Assert.Single(Abschnitt(auskunft, "Rollen in Organisationen")));
        Assert.Equal(
            new DatenauskunftZeile("Erste Hilfe (Jugendwerk Musterstadt)", $"eingesehen am {FreizeitHelfer.Heute(s.Factory):dd.MM.yyyy}, gültig bis 15.04.2027"),
            Assert.Single(Abschnitt(auskunft, "Nachweis-Bestätigungen")));
        Assert.Equal(new DatenauskunftZeile("Zeltlager Sommer 2027", "Leitung"), Assert.Single(Abschnitt(auskunft, "Mitarbeit in Freizeiten")));
        Assert.Contains(Abschnitt(auskunft, "Sicherheitsprotokoll"), z => z.Feld == "Anmeldung");

        // Nichts Fremdes: keine Mail-Adresse und kein Nachweis anderer Personen.
        Assert.DoesNotContain(Miriam, text, StringComparison.Ordinal);
        Assert.DoesNotContain(auskunft.Abschnitte.SelectMany(a => a.Zeilen), z => (z.Feld + z.Wert).Contains("Präventionsschulung", StringComparison.Ordinal));
        Assert.Equal(vorher + 1, await ProtokollAsync(s.Verbindung, SicherheitsEreignisArt.Export, jonas));
    }

    [Fact]
    public async Task Auskunft_enthaelt_Familie_Erinnerungen_und_den_entschluesselten_Hinweis_einer_Ablehnung()
    {
        await using var s = await ErstellenAsync(postgres, s3);
        await FamilienHelfer.KindAsync(s.JonasClient, "Emma");
        var fz = await NachweisAsync(s.JonasClient, "fuehrungszeugnis", "2025-11-03", Pdf);
        var einsicht = await AnfrageAsync(s.JonasClient);
        (await FreigebenAsync(s.JonasClient, einsicht)).Dispose();
        (await PruefenAsync(s.MiriamClient, s.FreizeitPfad, einsicht)).Dispose();
        using (var ablehnen = await AblehnenAsync(s.MiriamClient, s.FreizeitPfad, einsicht, fz, "Bitte ein neueres Zeugnis."))
        {
            Assert.Equal(HttpStatusCode.NoContent, ablehnen.StatusCode);
        }

        using (var erinnern = await PostAsync(s.MiriamClient, $"{s.FreizeitPfad}/erinnerungen", new Zeltlotse.Api.Contracts.Personen.NachweisErinnerungSenden(await s.PersonIdAsync(Jonas))))
        {
            Assert.Equal(HttpStatusCode.OK, erinnern.StatusCode);
        }

        var auskunft = (await s.JonasClient.GetFromJsonAsync<DatenauskunftAntwort>(Pfad, Abbruch))!;

        Assert.Equal(
            [new DatenauskunftZeile("Elternteil", "Jonas Hüttner"), new DatenauskunftZeile("Kind", "Emma Muster, geboren am 02.09.2017")],
            Abschnitt(auskunft, "Familie"));
        Assert.Equal(
            new DatenauskunftZeile($"Erinnert am {FreizeitHelfer.Heute(s.Factory):dd.MM.yyyy}", "von Miriam Zeltner"),
            Assert.Single(Abschnitt(auskunft, "Erinnerungen")));
        Assert.Contains(Abschnitt(auskunft, "Einsicht-Anfragen"), z => z.Wert.Contains("Hinweis zu Führungszeugnis: Bitte ein neueres Zeugnis.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Anmeldung_aelter_als_10_Minuten_ergibt_401_ohne_Export_und_eine_neue_Anmeldung_hilft()
    {
        var verbindung = await postgres.NeueDatenbankAsync();
        await using var factory = Factory(verbindung);
        using var client = await AngemeldeterClientAsync(factory, OrgHelfer.Jonas);
        (await PostAsync(client, "/api/ich/profil")).Dispose();
        factory.Uhr.Advance(TimeSpan.FromMinutes(11));

        using var alt = await client.GetAsync(Pfad, Abbruch);

        Assert.Equal(HttpStatusCode.Unauthorized, alt.StatusCode);
        await ProblemAsync(alt);
        Assert.Equal(0L, await ProtokollAsync(verbindung, SicherheitsEreignisArt.Export));

        await AnmeldenAsync(factory, client, OrgHelfer.Jonas);
        using var frisch = await client.GetAsync(Pfad, Abbruch);
        Assert.Equal(HttpStatusCode.OK, frisch.StatusCode);
    }

    [Fact]
    public async Task Ohne_Sitzung_401()
    {
        await using var factory = Factory(await postgres.NeueDatenbankAsync());
        using var client = factory.CreateClient();

        using var antwort = await client.GetAsync(Pfad, Abbruch);

        Assert.Equal(HttpStatusCode.Unauthorized, antwort.StatusCode);
    }

    private static IReadOnlyList<DatenauskunftZeile> Abschnitt(DatenauskunftAntwort auskunft, string titel) =>
        Assert.Single(auskunft.Abschnitte, a => a.Titel == titel).Zeilen;
}
