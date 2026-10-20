using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.SharedKernel.Push;
using Zeltlotse.Testing;
using Zeltlotse.Testmodul.Notizen;
using Zeltlotse.Testmodul.Pushes;

namespace Zeltlotse.SharedKernel.Tests;

/// <summary>Sendet nichts, hält die Pushes fest. Endpunkte mit „abgelaufen“ sind ungültig, mit „kaputt“ werfen sie.</summary>
internal sealed class PushSammler : IPushVersand
{
    public ConcurrentQueue<(PushZiel Ziel, PushInhalt Inhalt)> Pushes { get; } = new();

    public Task<PushErgebnis> SendenAsync(PushZiel ziel, PushInhalt inhalt, CancellationToken abbruch = default)
    {
        if (ziel.Endpunkt.Contains("kaputt", StringComparison.Ordinal))
        {
            throw new HttpRequestException("Push-Dienst nicht erreichbar.");
        }

        if (ziel.Endpunkt.Contains("abgelaufen", StringComparison.Ordinal))
        {
            return Task.FromResult(PushErgebnis.ZielUngueltig);
        }

        Pushes.Enqueue((ziel, inhalt));
        return Task.FromResult(PushErgebnis.Zugestellt);
    }
}

/// <summary>
/// Story 6-1: Push-Aufträge über die Outbox (AD-13, AD-21), gespiegelt von den Mail-Aufträgen: genau eine Zustellung an <c>Zeltlotse.Push</c>, in der
/// Outbox nur Kennungen; die Zustellung baut die Sendung mit dem Mandanten aus dem Umschlag und schickt sie an jedes Ziel einzeln. Ein Fehler bei einem Ziel
/// wird nur geloggt (die anderen bekommen den Push trotzdem, nichts wird doppelt gesendet), abgelaufene Ziele meldet sie dem Aufbau zum Abräumen. Dazu der
/// VAPID-Schlüssel und die erlaubten Push-Endpunkte.
/// </summary>
public sealed class PushAuftragTests(PostgresContainer postgres)
{
    private static readonly Guid OrgA = Guid.Parse("0199a000-0000-7000-8000-00000000000a");

    private static Task<TestUmgebung> UmgebungAsync(PostgresContainer postgres) =>
        TestUmgebung.ErstellenAsync(postgres, services => services.AddSingleton<IPushVersand, PushSammler>());

    private static PushSammler Sammler(TestUmgebung umgebung) => (PushSammler)umgebung.Dienste.GetRequiredService<IPushVersand>();

    private static PushAufbauStoerung Stoerung(TestUmgebung umgebung) => umgebung.Dienste.GetRequiredService<PushAufbauStoerung>();

    private static PushZiel Ziel(string endpunkt) => new(Guid.CreateVersion7(), endpunkt, "p256dh", "auth");

    [Fact]
    public async Task Push_Auftrag_landet_mit_der_Aenderung_in_der_Outbox_mit_genau_einer_Zustellung_und_nur_Kennungen()
    {
        await using var umgebung = await UmgebungAsync(postgres);
        var id = await umgebung.SendenAsync(new NotizAnlegen(OrgA, "Geheimer Text"), OrgA);

        await umgebung.SendenAsync(new NotizPushVormerken(id), OrgA);

        var push = await umgebung.RohLesenAsync(db => db.Outbox.SingleAsync(o => o.Typ == typeof(NotizPush).FullName));
        Assert.Equal((id, 2L, (Guid?)OrgA), (push.DatensatzId, push.Folgenummer, push.OrganisationId));
        using var inhalt = JsonDocument.Parse(push.Inhalt);
        Assert.Equal(["notizId"], inhalt.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.DoesNotContain("Geheimer", push.Inhalt, StringComparison.Ordinal);
        var zustellungen = await umgebung.RohLesenAsync(db => db.Zustellungen.Where(z => z.EreignisId == push.Id).Select(z => z.Empfaenger).ToListAsync());
        Assert.Equal([PushAuftraege.Empfaenger], zustellungen);
        Assert.Equal("Zeltlotse.Push", PushAuftraege.Empfaenger);
    }

    [Fact]
    public async Task Zustellung_baut_den_Push_mit_dem_Mandanten_und_schickt_ihn_an_jedes_Ziel_einmal_ohne_Inbox()
    {
        await using var umgebung = await UmgebungAsync(postgres);
        var (eins, zwei) = (Ziel("https://fcm.googleapis.com/eins"), Ziel("https://fcm.googleapis.com/zwei"));
        Stoerung(umgebung).Ziele.AddRange([eins, zwei]);
        var id = await umgebung.SendenAsync(new NotizAnlegen(OrgA, "Feuerholz holen"), OrgA);
        await umgebung.SendenAsync(new NotizPushVormerken(id), OrgA);

        await umgebung.AllesZustellenAsync();

        var gesendet = Sammler(umgebung).Pushes.ToList();
        Assert.Equal([eins, zwei], gesendet.Select(p => p.Ziel));
        Assert.All(gesendet, p => Assert.Equal(new PushInhalt("Notiz", $"Feuerholz holen | Mandant {OrgA}", "/app/notizen"), p.Inhalt));
        Assert.True(await umgebung.RohLesenAsync(db => db.Zustellungen.AllAsync(z => z.ErledigtAm != null)));
        Assert.False(await umgebung.RohLesenAsync(db => db.InboxEintraege.AnyAsync(i => i.Empfaenger == PushAuftraege.Empfaenger)));

        // Erneut zustellen schickt nichts doppelt.
        await umgebung.AllesZustellenAsync();
        Assert.Equal(2, Sammler(umgebung).Pushes.Count);
    }

    [Fact]
    public async Task Ein_Fehler_bei_einem_Ziel_haelt_die_anderen_nicht_auf_abgelaufene_Ziele_werden_gemeldet_nichts_doppelt()
    {
        await using var umgebung = await UmgebungAsync(postgres);
        var (kaputt, abgelaufen, gut) =
            (Ziel("https://fcm.googleapis.com/kaputt"), Ziel("https://fcm.googleapis.com/abgelaufen"), Ziel("https://fcm.googleapis.com/gut"));
        Stoerung(umgebung).Ziele.AddRange([kaputt, abgelaufen, gut]);
        var id = await umgebung.SendenAsync(new NotizAnlegen(OrgA, "Zeltplane"), OrgA);
        await umgebung.SendenAsync(new NotizPushVormerken(id), OrgA);

        await umgebung.AllesZustellenAsync();
        umgebung.Uhr.Advance(TimeSpan.FromHours(2));
        await umgebung.AllesZustellenAsync();

        Assert.Equal([gut], Sammler(umgebung).Pushes.Select(p => p.Ziel));
        Assert.Equal([abgelaufen.Id], Stoerung(umgebung).Ungueltig);
        var zustellung = await umgebung.RohLesenAsync(db => db.Zustellungen.SingleAsync(z => z.Empfaenger == PushAuftraege.Empfaenger));
        Assert.Equal((0, (string?)null), (zustellung.Versuche, zustellung.LetzterFehler));
        Assert.NotNull(zustellung.ErledigtAm);
    }

    [Fact]
    public async Task Scheitert_das_Abraeumen_ist_die_Zustellung_trotzdem_erledigt()
    {
        await using var umgebung = await UmgebungAsync(postgres);
        Stoerung(umgebung).Ziele.AddRange([Ziel("https://fcm.googleapis.com/abgelaufen"), Ziel("https://fcm.googleapis.com/gut")]);
        Stoerung(umgebung).AufraeumenScheitert = true;
        var id = await umgebung.SendenAsync(new NotizAnlegen(OrgA, "Zeltplane"), OrgA);
        await umgebung.SendenAsync(new NotizPushVormerken(id), OrgA);

        await umgebung.AllesZustellenAsync();

        Assert.Single(Sammler(umgebung).Pushes);
        Assert.NotNull((await umgebung.RohLesenAsync(db => db.Zustellungen.SingleAsync(z => z.Empfaenger == PushAuftraege.Empfaenger))).ErledigtAm);
    }

    [Fact]
    public async Task Ohne_Sendung_ist_die_Zustellung_erledigt_scheitert_der_Aufbau_wird_er_nach_der_Wartezeit_wiederholt()
    {
        await using var umgebung = await UmgebungAsync(postgres);
        Stoerung(umgebung).Ziele.Add(Ziel("https://fcm.googleapis.com/gut"));
        var id = await umgebung.SendenAsync(new NotizAnlegen(OrgA, "Zeltplane"), OrgA);
        Stoerung(umgebung).ScheitertEinmal(id);
        await umgebung.SendenAsync(new NotizPushVormerken(id), OrgA);

        await umgebung.AllesZustellenAsync();

        Assert.Empty(Sammler(umgebung).Pushes);
        var zustellung = await umgebung.RohLesenAsync(db => db.Zustellungen.SingleAsync(z => z.Empfaenger == PushAuftraege.Empfaenger));
        Assert.Equal((1, nameof(InvalidOperationException)), (zustellung.Versuche, zustellung.LetzterFehler));

        umgebung.Uhr.Advance(TimeSpan.FromMinutes(5));
        await umgebung.AllesZustellenAsync();
        Assert.Single(Sammler(umgebung).Pushes);

        // Ein zweiter Auftrag ohne Sendung ist sofort erledigt.
        Stoerung(umgebung).OhnePush = true;
        await umgebung.SendenAsync(new NotizPushVormerken(id), OrgA);
        await umgebung.AllesZustellenAsync();
        Assert.Single(Sammler(umgebung).Pushes);
        Assert.True(await umgebung.RohLesenAsync(db => db.Zustellungen.AllAsync(z => z.ErledigtAm != null)));
    }

    [Fact]
    public void VAPID_Schluessel_leitet_den_oeffentlichen_Punkt_ab_und_prueft_den_Wert()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var erwartet = ec.ExportParameters(includePrivateParameters: true);
        var konfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [VapidSchluessel.Konfigurationsschluessel] = Convert.ToBase64String(erwartet.D!) })
            .Build();

        var schluessel = VapidSchluessel.AusKonfiguration(konfiguration);

        Assert.Equal(Base64Url([0x04, .. erwartet.Q.X!, .. erwartet.Q.Y!]), schluessel.Oeffentlich);
        Assert.Equal(87, schluessel.Oeffentlich.Length);
        Assert.Equal(Base64Url(erwartet.D!), schluessel.Privat);
        Assert.Equal("VapidSchluessel", schluessel.ToString());
        foreach (var falsch in new[] { null, "", "kein base64", Convert.ToBase64String(new byte[31]), Convert.ToBase64String(new byte[32]) })
        {
            var fehler = Assert.Throws<InvalidOperationException>(() => VapidSchluessel.AusBase64(falsch));
            Assert.Contains(VapidSchluessel.Konfigurationsschluessel, fehler.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/abc", true)]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/abc", true)]
    [InlineData("https://web.push.apple.com/abc", true)]
    [InlineData("https://wns2-par02p.notify.windows.com/w/?token=abc", true)]
    [InlineData("http://fcm.googleapis.com/fcm/send/abc", false)]
    [InlineData("https://fcm.googleapis.com:8443/abc", false)]
    [InlineData("https://user:pw@fcm.googleapis.com/abc", false)]
    [InlineData("https://localhost/abc", false)]
    [InlineData("https://127.0.0.1/abc", false)]
    [InlineData("https://[::1]/abc", false)]
    [InlineData("https://example.org/abc", false)]
    [InlineData("https://fcm.googleapis.com.example.org/abc", false)]
    [InlineData("https://evilfcm.googleapis.com/abc", false)]
    [InlineData("https://ftp.example.org", false)]
    [InlineData("fcm.googleapis.com/abc", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Nur_bekannte_Push_Dienste_ueber_https_mit_Hostnamen(string? endpunkt, bool erlaubt) =>
        Assert.Equal(erlaubt, PushEndpunkte.IstErlaubt(endpunkt));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
