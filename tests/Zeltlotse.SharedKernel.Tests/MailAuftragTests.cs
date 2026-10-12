using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.SharedKernel.Mail;
using Zeltlotse.Testing;
using Zeltlotse.Testmodul.Mails;
using Zeltlotse.Testmodul.Notizen;

namespace Zeltlotse.SharedKernel.Tests;

/// <summary>Verschickt nichts, hält die Mails nur fest.</summary>
internal sealed class MailSammler : IMailVersand
{
    public ConcurrentQueue<MailInhalt> Mails { get; } = new();

    public Task SendenAsync(string an, string betreff, string text, CancellationToken abbruch = default)
    {
        Mails.Enqueue(new MailInhalt(an, betreff, text));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Mail-Aufträge über die Outbox (AD-13, Story 1-6): genau eine Zustellung an <c>Zeltlotse.Mail</c>, in der Outbox nur
/// Kennungen; die Zustellung baut die Mail mit dem Mandanten aus dem Umschlag, <c>null</c> heißt „nichts zu senden“, ein Fehler
/// wird nach der Wartezeit wiederholt. Keine Inbox.
/// </summary>
public sealed class MailAuftragTests(PostgresContainer postgres)
{
    private static readonly Guid OrgA = Guid.Parse("0199a000-0000-7000-8000-00000000000a");

    private static Task<TestUmgebung> UmgebungAsync(PostgresContainer postgres) =>
        TestUmgebung.ErstellenAsync(postgres, services => services.AddSingleton<IMailVersand, MailSammler>());

    private static MailSammler Sammler(TestUmgebung umgebung) => (MailSammler)umgebung.Dienste.GetRequiredService<IMailVersand>();

    [Fact]
    public async Task Mail_Auftrag_landet_mit_der_Aenderung_in_der_Outbox_mit_genau_einer_Zustellung_und_nur_Kennungen()
    {
        await using var umgebung = await UmgebungAsync(postgres);
        var id = await umgebung.SendenAsync(new NotizAnlegen(OrgA, "Geheimer Text"), OrgA);

        await umgebung.SendenAsync(new NotizMailVormerken(id), OrgA);

        var mail = await umgebung.RohLesenAsync(db => db.Outbox.SingleAsync(o => o.Typ == typeof(NotizMail).FullName));
        Assert.Equal((id, 2L, (Guid?)OrgA), (mail.DatensatzId, mail.Folgenummer, mail.OrganisationId));
        using var inhalt = JsonDocument.Parse(mail.Inhalt);
        Assert.Equal(["notizId"], inhalt.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.DoesNotContain("Geheimer", mail.Inhalt, StringComparison.Ordinal);
        var zustellungen = await umgebung.RohLesenAsync(db => db.Zustellungen.Where(z => z.EreignisId == mail.Id).Select(z => z.Empfaenger).ToListAsync());
        Assert.Equal([MailAuftraege.Empfaenger], zustellungen);
        Assert.Equal("Zeltlotse.Mail", MailAuftraege.Empfaenger);
    }

    [Fact]
    public async Task Zustellung_baut_die_Mail_mit_dem_Mandanten_aus_dem_Umschlag_und_verschickt_sie_ohne_Inbox()
    {
        await using var umgebung = await UmgebungAsync(postgres);
        var id = await umgebung.SendenAsync(new NotizAnlegen(OrgA, "Feuerholz holen"), OrgA);
        await umgebung.SendenAsync(new NotizMailVormerken(id), OrgA);

        await umgebung.AllesZustellenAsync();

        var gesendet = Assert.Single(Sammler(umgebung).Mails);
        Assert.Equal(new MailInhalt("notiz@example.org", "Notiz", $"Feuerholz holen | Mandant {OrgA}"), gesendet);
        Assert.True(await umgebung.RohLesenAsync(db => db.Zustellungen.AllAsync(z => z.ErledigtAm != null)));
        Assert.False(await umgebung.RohLesenAsync(db => db.InboxEintraege.AnyAsync(i => i.Empfaenger == MailAuftraege.Empfaenger)));

        // Erneut zustellen schickt nichts doppelt.
        await umgebung.AllesZustellenAsync();
        Assert.Single(Sammler(umgebung).Mails);
    }

    [Fact]
    public async Task Liefert_der_Aufbau_null_ist_die_Zustellung_ohne_Mail_erledigt()
    {
        await using var umgebung = await UmgebungAsync(postgres);
        var id = await umgebung.SendenAsync(new NotizAnlegen(OrgA, "ohne Konto"), OrgA);
        umgebung.Dienste.GetRequiredService<MailAufbauStoerung>().OhneMail(id);
        await umgebung.SendenAsync(new NotizMailVormerken(id), OrgA);

        await umgebung.AllesZustellenAsync();

        Assert.Empty(Sammler(umgebung).Mails);
        var zustellung = await umgebung.RohLesenAsync(db => db.Zustellungen.SingleAsync(z => z.Empfaenger == MailAuftraege.Empfaenger));
        Assert.NotNull(zustellung.ErledigtAm);
        Assert.Equal(0, zustellung.Versuche);
    }

    [Fact]
    public async Task Scheitert_der_Aufbau_einmal_verschickt_der_zweite_Versuch_nach_der_Wartezeit_genau_eine_Mail()
    {
        await using var umgebung = await UmgebungAsync(postgres);
        var id = await umgebung.SendenAsync(new NotizAnlegen(OrgA, "Zeltplane"), OrgA);
        umgebung.Dienste.GetRequiredService<MailAufbauStoerung>().ScheitertEinmal(id);
        await umgebung.SendenAsync(new NotizMailVormerken(id), OrgA);

        await umgebung.AllesZustellenAsync();

        Assert.Empty(Sammler(umgebung).Mails);
        var zustellung = await umgebung.RohLesenAsync(db => db.Zustellungen.SingleAsync(z => z.Empfaenger == MailAuftraege.Empfaenger));
        Assert.Equal((1, nameof(InvalidOperationException)), (zustellung.Versuche, zustellung.LetzterFehler));
        Assert.Null(zustellung.ErledigtAm);

        umgebung.Uhr.Advance(TimeSpan.FromMinutes(5));
        await umgebung.AllesZustellenAsync();
        await umgebung.AllesZustellenAsync();

        Assert.Single(Sammler(umgebung).Mails);
    }
}
