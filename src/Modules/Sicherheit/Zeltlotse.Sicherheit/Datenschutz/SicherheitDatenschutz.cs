using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Sicherheit;
using Zeltlotse.Personen.Contracts.Anmeldung;
using Zeltlotse.Personen.Contracts.Events;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.Sicherheit.Daten;
using Zeltlotse.Sicherheit.Vorfaelle;

namespace Zeltlotse.Sicherheit.Datenschutz;

/// <summary>
/// Story 2-9: Empfänger von <see cref="PersonGeloescht"/> (Tabelle Ereignis-Fluss, AD-16): löscht in allen Organisationen die
/// Schutzkonzept-Bestätigungen der Person. Sie beschreiben die Person selbst (wie Team-Notizen über sie), darum kein Platzhalter. Story 2-10:
/// Einträge des Vorfall-Protokolls sind aufbewahrungspflichtig für die Organisation; sie bleiben, die Kennungen der Person (betroffen oder
/// verfassend) werden zum Platzhalter, der Text ist mit ihrem vernichteten Schlüssel nicht mehr lesbar. Kein Benutzer-Command.
/// </summary>
internal sealed record SicherheitPersonLoeschen(Umschlag Umschlag, Guid PersonId)
    : ICommand, IModulCommand<SicherheitDbContext>, IEreignisEmpfaenger<SicherheitPersonLoeschen, PersonGeloescht>
{
    public static SicherheitPersonLoeschen Aus(Umschlag umschlag, PersonGeloescht ereignis) => new(umschlag, ereignis.PersonId);
}

internal sealed class SicherheitPersonLoeschenHandler(SicherheitDbContext db) : ICommandHandler<SicherheitPersonLoeschen>
{
    public async ValueTask<Unit> Handle(SicherheitPersonLoeschen command, CancellationToken cancellationToken)
    {
        await db.SchutzkonzeptBestaetigungen.IgnoreQueryFilters().Where(b => b.PersonId == command.PersonId).ExecuteDeleteAsync(cancellationToken);
        await db.VorfallEintraege.IgnoreQueryFilters().Where(e => e.PersonId == command.PersonId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PersonId, GeloeschtePerson.Id), cancellationToken);
        await db.VorfallEintraege.IgnoreQueryFilters().Where(e => e.VerfasstVon == command.PersonId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.VerfasstVon, GeloeschtePerson.Id), cancellationToken);
        return Unit.Value;
    }
}

/// <summary>
/// Datenauskunft von Sicherheit (AD-16, Story 2-9): je Schutzkonzept-Bestätigung der Person die Zeile „Als gelesen bestätigt“ mit dem
/// Zeitpunkt. Story 2-10: Abschnitt „Vorfall-Protokoll“ mit den Einträgen über die Person und die Kinder ihrer Familie (Art, Zeitpunkt und
/// Text) und je selbst verfasstem Eintrag „Eintrag verfasst“ mit dem Zeitpunkt. Ohne Mandantenfilter, nur diese Zeilen.
/// </summary>
internal sealed class SicherheitAuskunft(SicherheitLeseDbContext db, IVertretung vertretung, IVerschluesselung verschluesselung) : IDatenauskunft
{
    public async Task<IReadOnlyList<AuskunftAbschnitt>> AuskunftAsync(Guid personId, CancellationToken abbruch)
    {
        var bestaetigungen = await db.SchutzkonzeptBestaetigungen.IgnoreQueryFilters()
            .Where(b => b.PersonId == personId)
            .OrderBy(b => b.GelesenAm)
            .ThenBy(b => b.Id)
            .Select(b => b.GelesenAm)
            .ToListAsync(abbruch);
        return
        [
            new AuskunftAbschnitt("Schutzkonzept", [.. bestaetigungen.Select(am => new AuskunftZeile("Als gelesen bestätigt", AuskunftFormat.Zeitpunkt(am)))]),
            new AuskunftAbschnitt("Vorfall-Protokoll", await VorfaelleAsync(personId, abbruch)),
        ];
    }

    private async Task<List<AuskunftZeile>> VorfaelleAsync(Guid personId, CancellationToken abbruch)
    {
        List<Guid> ids = [.. await vertretung.PersonenAsync(personId, abbruch)];
        var eintraege = await db.VorfallEintraege.IgnoreQueryFilters()
            .Where(e => ids.Contains(e.PersonId) || e.VerfasstVon == personId)
            .OrderBy(e => e.ErfasstAm)
            .ThenBy(e => e.Id)
            .ToListAsync(abbruch);
        var zeilen = new List<AuskunftZeile>();
        foreach (var eintrag in eintraege)
        {
            if (ids.Contains(eintrag.PersonId) && await VorfallLesen.TextAsync(verschluesselung, eintrag.Text, abbruch) is { } text)
            {
                zeilen.Add(new AuskunftZeile(ArtText(eintrag.Art), $"{AuskunftFormat.Zeitpunkt(eintrag.ErfasstAm)}: {text}"));
            }

            if (eintrag.VerfasstVon == personId)
            {
                zeilen.Add(new AuskunftZeile("Eintrag verfasst", AuskunftFormat.Zeitpunkt(eintrag.ErfasstAm)));
            }
        }

        return zeilen;
    }

    private static string ArtText(VorfallArt art) => art switch
    {
        VorfallArt.Geschehen => "Was passiert ist",
        VorfallArt.Medikament => "Medikament gegeben",
        VorfallArt.Entnahme => "Aus dem Erste-Hilfe-Kasten entnommen",
        _ => art.ToString(),
    };
}

/// <summary>
/// Täglicher Lösch-Lauf von Sicherheit (AD-16): Bestätigungen und (Story 2-10) Einträge des Vorfall-Protokolls der Freizeiten, deren Daten
/// nach der Frist abgelaufen sind.
/// </summary>
internal sealed class SicherheitLoeschlauf(ISender sender) : ILoeschlauf
{
    public async Task LaufAsync(LoeschAuftrag auftrag, CancellationToken abbruch)
    {
        if (auftrag.FreizeitenMitAbgelaufenenDaten.Count > 0)
        {
            await sender.Send(new SchutzkonzeptBestaetigungenLoeschen(auftrag.FreizeitenMitAbgelaufenenDaten), abbruch);
            await sender.Send(new VorfallEintraegeLoeschen(auftrag.FreizeitenMitAbgelaufenenDaten), abbruch);
        }
    }
}

/// <summary>Schutzkonzept-Bestätigungen der Freizeiten, deren Daten nach der Frist der Organisation abgelaufen sind, endgültig löschen.</summary>
internal sealed record SchutzkonzeptBestaetigungenLoeschen(IReadOnlyList<Guid> FreizeitIds) : ICommand, IModulCommand<SicherheitDbContext>;

internal sealed class SchutzkonzeptBestaetigungenLoeschenHandler(SicherheitDbContext db) : ICommandHandler<SchutzkonzeptBestaetigungenLoeschen>
{
    public async ValueTask<Unit> Handle(SchutzkonzeptBestaetigungenLoeschen command, CancellationToken cancellationToken)
    {
        var freizeiten = command.FreizeitIds.ToList();
        await db.SchutzkonzeptBestaetigungen.IgnoreQueryFilters().Where(b => freizeiten.Contains(b.FreizeitId)).ExecuteDeleteAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>Story 2-10: Einträge des Vorfall-Protokolls der Freizeiten, deren Daten nach der Frist abgelaufen sind, endgültig löschen.</summary>
internal sealed record VorfallEintraegeLoeschen(IReadOnlyList<Guid> FreizeitIds) : ICommand, IModulCommand<SicherheitDbContext>;

internal sealed class VorfallEintraegeLoeschenHandler(SicherheitDbContext db) : ICommandHandler<VorfallEintraegeLoeschen>
{
    public async ValueTask<Unit> Handle(VorfallEintraegeLoeschen command, CancellationToken cancellationToken)
    {
        var freizeiten = command.FreizeitIds.ToList();
        await db.VorfallEintraege.IgnoreQueryFilters().Where(e => freizeiten.Contains(e.FreizeitId)).ExecuteDeleteAsync(cancellationToken);
        return Unit.Value;
    }
}
