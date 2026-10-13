using System.Collections.Concurrent;
using System.Reflection;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Api.Contracts.Sicherheit;
using Zeltlotse.Freizeiten.Contracts.Events;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.Sicherheit.Daten;
using Zeltlotse.Sicherheit.Sicherheitskette;
using Zeltlotse.Sicherheit.Vorfaelle;
using Zeltlotse.Testing;
using VorfallEintrag = Zeltlotse.Sicherheit.Vorfaelle.VorfallEintrag;

namespace Zeltlotse.Sicherheit.Tests;

/// <summary>
/// Story 2-10, Vorfall-Protokoll (FR-31, AD-14, AD-16, AD-18, AD-19): die Entität (Schlüssel der betroffenen Person, einmaliges Archivieren),
/// die Formprüfung, der Command ohne Text in Logs, der Mandantenfilter, der Empfänger von <c>FreizeitAbgeschlossen</c> und die Gleichheit der
/// Rechteliste des Archivs mit Organisationen.
/// </summary>
public sealed class VorfaelleTests(PostgresContainer postgres)
{
    private static CancellationToken Abbruch => TestContext.Current.CancellationToken;

    // ---------- Entität ----------

    [Fact]
    public async Task Anlegen_verlangt_den_Schluessel_der_betroffenen_Person()
    {
        await using var factory = await FactoryAsync();
        var (person, andere) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var text = await VerschluesselnAsync(factory, person, "Knie");

        var eintrag = VorfallEintrag.Anlegen(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), person, andere, VorfallArt.Geschehen, text, DateTimeOffset.UtcNow);

        Assert.Equal((person, andere, VorfallArt.Geschehen, (DateTimeOffset?)null), (eintrag.PersonId, eintrag.VerfasstVon, eintrag.Art, eintrag.ArchiviertAm));
        Assert.Throws<InvalidOperationException>(() => VorfallEintrag.Anlegen(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), andere, person, VorfallArt.Geschehen, text, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Archivieren_setzt_den_Zeitpunkt_nur_einmal()
    {
        await using var factory = await FactoryAsync();
        var person = Guid.CreateVersion7();
        var eintrag = VorfallEintrag.Anlegen(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), person, person, VorfallArt.Entnahme,
            await VerschluesselnAsync(factory, person, "Pflaster"), DateTimeOffset.UtcNow);
        var erster = new DateTimeOffset(2027, 8, 14, 10, 0, 0, TimeSpan.Zero);

        eintrag.Archivieren(erster);
        eintrag.Archivieren(erster.AddDays(1));

        Assert.Equal(erster, eintrag.ArchiviertAm);
    }

    // ---------- Formprüfung ----------

    [Fact]
    public void Validator_verlangt_Kennung_bekannte_Art_und_Text_bis_2000_Zeichen()
    {
        var validator = new VorfallAnlegenValidator();
        VorfallAnlegenCommand Command(Guid? id = null, VorfallArt art = VorfallArt.Geschehen, string? text = "Knie") =>
            new(Guid.CreateVersion7(), id ?? Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), art, text);

        Assert.True(validator.Validate(Command()).IsValid);
        Assert.True(validator.Validate(Command(text: $"  {new string('x', VorfallFelder.TextMaximaleLaenge)}  ")).IsValid);
        foreach (var (command, feld) in new[]
                 {
                     (Command(id: Guid.Empty), VorfallFelder.Id),
                     (Command(art: (VorfallArt)9), VorfallFelder.Art),
                     (Command(art: 0), VorfallFelder.Art),
                     (Command(text: null), VorfallFelder.Text),
                     (Command(text: "   "), VorfallFelder.Text),
                     (Command(text: new string('x', VorfallFelder.TextMaximaleLaenge + 1)), VorfallFelder.Text),
                 })
        {
            Assert.Equal(feld, Assert.Single(validator.Validate(command).Errors).PropertyName);
        }
    }

    [Fact]
    public void Der_Command_gibt_weder_Text_noch_Personen_aus()
    {
        var (person, verfasser) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        var text = new VorfallAnlegenCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), person, verfasser, VorfallArt.Medikament, "Ibuprofen 200 mg").ToString();

        Assert.DoesNotContain("Ibuprofen", text, StringComparison.Ordinal);
        Assert.DoesNotContain(person.ToString(), text, StringComparison.Ordinal);
        Assert.DoesNotContain(verfasser.ToString(), text, StringComparison.Ordinal);
    }

    // ---------- Mandantenfilter ----------

    [Fact]
    public async Task Mandantenfilter_der_Lese_Kontext_liefert_je_Mandant_nur_eigene_Eintraege()
    {
        await using var factory = await FactoryAsync();
        var (orgA, orgB, x, z) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        await EintragAsync(factory, orgA, x);
        await EintragAsync(factory, orgB, z);

        foreach (var (organisation, erwartet) in new (Guid?, Guid[])[] { (orgA, [x]), (orgB, [z]), (null, []) })
        {
            Assert.Equal(erwartet, await LesenAsync(factory, organisation, db => db.VorfallEintraege.Select(e => e.FreizeitId).ToListAsync(Abbruch)));
        }
    }

    // ---------- Empfänger FreizeitAbgeschlossen ----------

    [Fact]
    public async Task VorfaelleArchivieren_archiviert_die_Eintraege_der_Freizeit_einmal_die_anderer_nicht()
    {
        await using var factory = await FactoryAsync();
        var (orgA, orgB, x, y, z) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        await EintragAsync(factory, orgA, x);
        await EintragAsync(factory, orgA, x);
        await EintragAsync(factory, orgA, y);
        await EintragAsync(factory, orgB, z);
        var erster = factory.Uhr.GetUtcNow();

        await ZustellenAsync(factory, orgA, x);
        factory.Uhr.Advance(TimeSpan.FromHours(1));
        await ZustellenAsync(factory, orgA, x);

        var inA = await LesenAsync(factory, orgA, db => db.VorfallEintraege.Select(e => new { e.FreizeitId, e.ArchiviertAm }).ToListAsync(Abbruch));
        Assert.All(inA.Where(e => e.FreizeitId == x), e => Assert.Equal(erster, e.ArchiviertAm));
        Assert.Equal(2, inA.Count(e => e.FreizeitId == x));
        Assert.Null(Assert.Single(inA, e => e.FreizeitId == y).ArchiviertAm);
        Assert.Null(Assert.Single(await LesenAsync(factory, orgB, db => db.VorfallEintraege.ToListAsync(Abbruch))).ArchiviertAm);
    }

    [Fact]
    public async Task Das_Modul_meldet_VorfaelleArchivieren_als_Empfaenger_von_FreizeitAbgeschlossen_an()
    {
        await using var factory = await FactoryAsync();

        var eintraege = factory.Services.GetServices<EmpfaengerEintrag>();

        Assert.Single(eintraege, e => e.EreignisTyp == typeof(FreizeitAbgeschlossen) && e.CommandTyp == typeof(VorfaelleArchivieren));
    }

    // ---------- Rechte ----------

    [Fact]
    public void Archiv_ist_gleich_der_Liste_VorfallArchiv_in_Organisationen()
    {
        var werte = (Array)Assembly.Load("Zeltlotse.Organisationen").GetType("Zeltlotse.Organisationen.Rollen.OrgRechte", throwOnError: true)!
            .GetField("VorfallArchiv", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

        Assert.Equal(werte.Cast<object>().Order(), SicherheitRechte.Archiv.Cast<object>().Order());
    }

    // ---------- Helfer ----------

    private async Task<ZeltlotseApiFactory> FactoryAsync()
    {
        var factory = new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync());
        // Personen-Schlüssel ohne Profil: eine Quelle im Speicher, gleicher Hauptschlüssel wie die Verschlüsselung.
        factory.ZusatzDienste.Add(s => s.AddSingleton<IPersonenSchluesselQuelle>(sp => new SpeicherSchluesselQuelle(sp.GetRequiredService<Hauptschluessel>())));
        _ = factory.CreateClient();
        return factory;
    }

    private static async Task<Geschuetzt> VerschluesselnAsync(ZeltlotseApiFactory factory, Guid person, string text)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IVerschluesselung>().VerschluesselnAsync(person, text, Abbruch);
    }

    private static async Task EintragAsync(ZeltlotseApiFactory factory, Guid organisation, Guid freizeit)
    {
        var person = Guid.CreateVersion7();
        var text = await VerschluesselnAsync(factory, person, "Knie geschnitten");
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        var db = scope.ServiceProvider.GetRequiredService<SicherheitDbContext>();
        db.VorfallEintraege.Add(VorfallEintrag.Anlegen(
            Guid.CreateVersion7(), organisation, freizeit, person, Guid.CreateVersion7(), VorfallArt.Geschehen, text, factory.Uhr.GetUtcNow()));
        await db.SaveChangesAsync(Abbruch);
    }

    /// <summary>Wie der Zustelldienst: eigener Scope, Mandant aus dem Umschlag (AD-4).</summary>
    private static async Task ZustellenAsync(ZeltlotseApiFactory factory, Guid organisation, Guid freizeit)
    {
        var umschlag = new Umschlag(Guid.CreateVersion7(), factory.Uhr.GetUtcNow(), organisation, freizeit, 1);
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(VorfaelleArchivieren.Aus(umschlag, new FreizeitAbgeschlossen(freizeit)), Abbruch);
    }

    private static async Task<T> LesenAsync<T>(ZeltlotseApiFactory factory, Guid? organisation, Func<SicherheitLeseDbContext, Task<T>> abfrage)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MandantenKontext>().Setzen(organisation);
        return await abfrage(scope.ServiceProvider.GetRequiredService<SicherheitLeseDbContext>());
    }

    /// <summary>Personen-Schlüssel im Speicher: je Person beim ersten Zugriff ein neuer, mit dem Hauptschlüssel umhüllt.</summary>
    private sealed class SpeicherSchluesselQuelle(Hauptschluessel hauptschluessel) : IPersonenSchluesselQuelle
    {
        private readonly ConcurrentDictionary<Guid, byte[]> _schluessel = new();

        public Task<byte[]?> UmhuelltenSchluesselLesenAsync(Guid personId, CancellationToken abbruch = default) =>
            Task.FromResult<byte[]?>(_schluessel.GetOrAdd(personId, hauptschluessel.NeuenPersonenSchluesselUmhuellen));
    }
}
