using Mediator;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zeltlotse.Personen.Daten;
using Zeltlotse.Personen.Familien;
using Zeltlotse.Personen.Nachweise;
using Zeltlotse.Personen.Profil;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Dateien;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;

namespace Zeltlotse.Personen.Datenschutz;

/// <summary>
/// Konto löschen (FR-14, AD-16): löscht in einer Transaktion die Person und alle personeneigenen Daten endgültig, vernichtet den
/// Personen-Schlüssel (Crypto-Shredding) und legt <c>PersonGeloescht</c> ab. Ist die Person einziger Elternteil ihrer Familie,
/// werden alle Kinderprofile der Familie ebenso gelöscht (je mit eigenem Ereignis), danach Familie, Einladungen und Übergaben der
/// Familie. Mit zweitem Elternteil bleiben die Kinder bei ihm. Bezüge in Daten anderer ersetzt der Platzhalter
/// <see cref="GeloeschtePerson"/>. Die erneute Anmeldung (AD-25) prüft der Host am Endpunkt.
/// </summary>
internal sealed record PersonLoeschen(Guid PersonId) : ICommand<PersonLoeschung>, IModulCommand<PersonenDbContext>, IBenutzerCommand;

/// <summary>Ergebnis von <see cref="PersonLoeschen"/>.</summary>
internal abstract record PersonLoeschung
{
    private PersonLoeschung()
    {
    }

    /// <summary>
    /// Gelöscht: die Person und, falls einziger Elternteil, die Kinder; dazu die Kennungen der Nachweise, deren Dateien der Endpunkt
    /// erst nach dem Commit löscht.
    /// </summary>
    public sealed record Geloescht(IReadOnlyList<Guid> PersonIds, IReadOnlyList<Guid> NachweisIds) : PersonLoeschung;

    /// <summary>Zur Sitzung gibt es kein Profil: <c>404</c>.</summary>
    public sealed record NichtGefunden : PersonLoeschung;
}

internal sealed class PersonLoeschenHandler(PersonenDbContext db) : ICommandHandler<PersonLoeschen, PersonLoeschung>
{
    public async ValueTask<PersonLoeschung> Handle(PersonLoeschen command, CancellationToken cancellationToken)
    {
        // Dieselbe Sperre je Person wie „Kind anlegen“, „Elternteil einladen“ und „Profil übernehmen“.
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({command.PersonId.ToString()}, 0))", cancellationToken);
        var person = await FamilienSperre.PersonAsync(db.Personen, command.PersonId, cancellationToken);
        if (person is null)
        {
            return new PersonLoeschung.NichtGefunden();
        }

        var zuLoeschen = new List<Person> { person };
        Guid? familieLoeschen = null;
        var elternteil = await db.Elternteile.SingleOrDefaultAsync(e => e.PersonId == person.Id, cancellationToken);
        if (elternteil is not null)
        {
            await FamilienSperre.FamilieAsync(db.Familien, elternteil.FamilieId, cancellationToken);
            if (await db.Elternteile.CountAsync(e => e.FamilieId == elternteil.FamilieId, cancellationToken) == 1)
            {
                // AD-7: Kinderprofile ohne Elternteil hätten keinen Besitzer; sie werden mitgelöscht.
                zuLoeschen.AddRange(await db.Personen.Where(p => p.FamilieId == elternteil.FamilieId).ToListAsync(cancellationToken));
                familieLoeschen = elternteil.FamilieId;
            }
        }

        var ids = zuLoeschen.Select(p => p.Id).ToList();
        var nachweise = await db.Nachweise.Where(n => ids.Contains(n.PersonId)).Select(n => n.Id).ToListAsync(cancellationToken);

        await FremdeBezuegeErsetzenAsync(ids, cancellationToken);
        await EigeneDatenLoeschenAsync(ids, cancellationToken);
        if (familieLoeschen is { } familie)
        {
            await db.Uebergaben.Where(u => u.FamilieId == familie).ExecuteDeleteAsync(cancellationToken);
            await db.FamilienEinladungen.Where(e => e.FamilieId == familie).ExecuteDeleteAsync(cancellationToken);
            await db.Elternteile.Where(e => e.FamilieId == familie).ExecuteDeleteAsync(cancellationToken);
            await db.Familien.Where(f => f.Id == familie).ExecuteDeleteAsync(cancellationToken);
        }

        // Die Zeilen der Personen entfernt die Outbox-Stufe im selben SaveChanges, zusammen mit den Ereignissen.
        foreach (var geloescht in zuLoeschen)
        {
            geloescht.Loeschen();
            db.Personen.Remove(geloescht);
        }

        // Die Dateien löscht der Endpunkt erst nach dem Commit; scheitert die Transaktion, bleiben sie erhalten.
        return new PersonLoeschung.Geloescht(ids, nachweise);
    }

    /// <summary>Personeneigene Daten (Lösch-Klasse „personeneigen“): sofort endgültig, über alle Organisationen hinweg.</summary>
    private async Task EigeneDatenLoeschenAsync(List<Guid> ids, CancellationToken abbruch)
    {
        // Die Posten der eigenen Einsichten löscht die Datenbank mit (Fremdschlüssel mit Kaskade).
        await db.Einsichten.IgnoreQueryFilters().Where(e => ids.Contains(e.PersonId)).ExecuteDeleteAsync(abbruch);
        await db.Erinnerungen.IgnoreQueryFilters().Where(e => ids.Contains(e.PersonId)).ExecuteDeleteAsync(abbruch);
        await db.Uebergaben.Where(u => ids.Contains(u.PersonId)).ExecuteDeleteAsync(abbruch);
        await db.FreizeitRollen.IgnoreQueryFilters().Where(r => ids.Contains(r.PersonId)).ExecuteDeleteAsync(abbruch);
        await db.EinsichtProtokoll.Where(e => ids.Contains(e.BetroffenePersonId)).ExecuteDeleteAsync(abbruch);
        await db.Elternteile.Where(e => ids.Contains(e.PersonId)).ExecuteDeleteAsync(abbruch);
        await db.Nachweise.Where(n => ids.Contains(n.PersonId)).ExecuteDeleteAsync(abbruch);
        await db.PersonenSchluessel.Where(s => ids.Contains(s.PersonId)).ExecuteDeleteAsync(abbruch);
    }

    /// <summary>Bezüge in Daten anderer: der Platzhalter statt der <c>PersonId</c> (AD-16).</summary>
    private async Task FremdeBezuegeErsetzenAsync(List<Guid> ids, CancellationToken abbruch)
    {
        Guid? platzhalter = GeloeschtePerson.Id;
        await db.Einsichten.IgnoreQueryFilters().Where(e => e.PruefendePersonId != null && ids.Contains(e.PruefendePersonId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PruefendePersonId, platzhalter), abbruch);
        await db.Einsichten.IgnoreQueryFilters().Where(e => e.AngefragtVon != null && ids.Contains(e.AngefragtVon.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.AngefragtVon, platzhalter), abbruch);
        await db.Erinnerungen.IgnoreQueryFilters().Where(e => ids.Contains(e.ErinnertVon))
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.ErinnertVon, GeloeschtePerson.Id), abbruch);
        await db.EinsichtProtokoll.Where(e => ids.Contains(e.LesendePersonId))
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.LesendePersonId, GeloeschtePerson.Id), abbruch);
        await db.FamilienEinladungen.Where(e => ids.Contains(e.EingeladenVon))
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.EingeladenVon, GeloeschtePerson.Id), abbruch);
        await db.Uebergaben.Where(u => ids.Contains(u.EingeladenVon))
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.EingeladenVon, GeloeschtePerson.Id), abbruch);
    }
}

/// <summary>
/// <c>POST /api/ich/profil/loeschen</c> (erneute Anmeldung, AD-25; Antiforgery): <c>204</c>; die Sitzung endet (Cookie gelöscht),
/// das Sicherheitsprotokoll bekommt <c>Loeschung</c>. Konten, Organisationen und Freizeiten löschen ihre Teile über
/// <c>PersonGeloescht</c>; danach gilt auch keine andere Sitzung des Kontos mehr. Ohne Profil <c>404</c>. Die Nachweis-Dateien
/// löscht der Endpunkt nach dem Commit, je Datei ohne Abbruch: Ein Fehler wird nur mit Kennungen geloggt; ohne den vernichteten
/// Schlüssel ist die Datei ohnehin unlesbar.
/// </summary>
internal static partial class PersonLoeschenEndpunkt
{
    public static async Task<Results<NoContent, NotFound>> Loeschen(
        IAngemeldet angemeldet, ISender sender, ISicherheitsprotokoll protokoll, IGeschuetzteDateien dateien, ILoggerFactory logger,
        HttpContext kontext, CancellationToken abbruch)
    {
        var personId = FamilieAntworten.PersonId(angemeldet);
        if (await sender.Send(new PersonLoeschen(personId), abbruch) is not PersonLoeschung.Geloescht geloescht)
        {
            return TypedResults.NotFound();
        }

        var log = logger.CreateLogger(typeof(PersonLoeschenEndpunkt));
        foreach (var nachweisId in geloescht.NachweisIds)
        {
            try
            {
                await dateien.LoeschenAsync(Nachweis.DateiPraefix, nachweisId, CancellationToken.None);
            }
            catch (Exception fehler)
            {
                DateiNichtGeloescht(log, nachweisId, fehler.GetType().Name);
            }
        }

        await protokoll.SchreibenAsync(SicherheitsEreignisArt.Loeschung, personId, abbruch: abbruch);
        await kontext.SignOutAsync(Sitzung.Schema);
        return TypedResults.NoContent();
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Nachweis-Datei {NachweisId} nach dem Löschen der Person nicht gelöscht ({FehlerTyp}).")]
    private static partial void DateiNichtGeloescht(ILogger logger, Guid nachweisId, string fehlerTyp);
}
