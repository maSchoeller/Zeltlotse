using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.SharedKernel.Mail;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Outbox;

namespace Zeltlotse.SharedKernel.Daten;

/// <summary>
/// Schreibender DbContext eines Moduls. Nur Command-Handler erhalten ihn (AD-27); Lösch-Dienst und Datenauskunft brauchen ihn
/// nicht: Der Lösch-Dienst schickt je Modul einen internen Command, die Datenauskunft liest über den Lese-Kontext.
/// Trägt die Outbox- und Inbox-Tabellen des Modulschemas (AD-3, AD-4).
/// </summary>
public abstract class ModulDbContext(DbContextOptions optionen, IMandantenKontext mandantenKontext)
    : ModulDbContextBasis(optionen, mandantenKontext)
{
    internal static readonly JsonSerializerOptions JsonOptionen = new(JsonSerializerDefaults.Web);

    private readonly IMandantenKontext _mandantenKontext = mandantenKontext;

    internal DbSet<OutboxNachricht> Outbox => Set<OutboxNachricht>();

    internal DbSet<Zustellung> Zustellungen => Set<Zustellung>();

    internal DbSet<InboxEintrag> InboxEintraege => Set<InboxEintrag>();

    internal DbSet<InboxStand> InboxStaende => Set<InboxStand>();

    private protected override void ZusatzModellKonfigurieren(ModelBuilder modelBuilder) =>
        OutboxModell.Konfigurieren(modelBuilder);

    /// <summary>
    /// Outbox-Stufe: sammelt die Ereignisse aller verfolgten Aggregate ein (auch gelöschter, z. B. <c>PersonGeloescht</c>), legt je
    /// Ereignis die Outbox-Zeile und den Zustellstand je Empfänger an und speichert alles in einem <c>SaveChanges</c> (AD-28).
    /// </summary>
    internal async Task OutboxSchreibenUndSpeichernAsync(TimeProvider uhr, EreignisKatalog katalog, CancellationToken abbruch)
    {
        var jetzt = uhr.GetUtcNow();

        var aggregate = ChangeTracker.Entries<Aggregat>()
            .Select(e => e.Entity)
            .Where(a => a.AbgelegteEreignisse.Count > 0)
            .ToList();

        foreach (var aggregat in aggregate)
        {
            var organisationId = aggregat is IOrganisationsEigen eigen ? eigen.OrganisationId : _mandantenKontext.OrganisationId;
            foreach (var abgelegt in aggregat.AbgelegteEreignisse)
            {
                var typ = abgelegt.Ereignis.GetType();
                var nachricht = new OutboxNachricht
                {
                    Id = Kennung.Neu(uhr),
                    Typ = typ.FullName!,
                    Inhalt = JsonSerializer.Serialize(abgelegt.Ereignis, typ, JsonOptionen),
                    Zeitpunkt = jetzt,
                    OrganisationId = organisationId,
                    DatensatzId = aggregat.Id,
                    Folgenummer = abgelegt.Folgenummer,
                };
                Outbox.Add(nachricht);

                // Ein Mail-Auftrag hat genau eine Zustellung an den festen Empfänger der Mail-Zustellung (AD-13).
                IEnumerable<string> empfaengerNamen = abgelegt.Ereignis is IMailAuftrag
                    ? [MailAuftraege.Empfaenger]
                    : katalog.EmpfaengerFuer(typ).Select(e => e.Empfaenger);
                foreach (var empfaenger in empfaengerNamen)
                {
                    Zustellungen.Add(new Zustellung
                    {
                        EreignisId = nachricht.Id,
                        Empfaenger = empfaenger,
                        NaechsterVersuch = jetzt,
                    });
                }
            }

        }

        await SaveChangesAsync(abbruch);

        // Erst nach erfolgreichem Speichern leeren, sonst gingen Ereignisse bei einem Fehler verloren.
        foreach (var aggregat in aggregate)
        {
            aggregat.EreignisseLeeren();
        }
    }
}
