using Microsoft.EntityFrameworkCore;
using Zeltlotse.Personen.Daten;
using Zeltlotse.Personen.Nachweise;
using Zeltlotse.Personen.Zugriffsprotokoll;
using Zeltlotse.SharedKernel.Datenschutz;
using NachweisArt = Zeltlotse.Personen.Contracts.Events.NachweisArt;

namespace Zeltlotse.Personen.Datenschutz;

/// <summary>
/// Datenauskunft von Personen (AD-16, FR-14): Profil, Familie, Nachweise, Einsicht-Anfragen (mit entschlüsseltem Hinweis),
/// Erinnerungen und Zugriffsprotokoll der Person. Liest nur über den Lese-Kontext; organisationseigene Zeilen
/// (Einsichten, Erinnerungen) ohne Mandantenfilter, aber nur die der Person (AD-6).
/// </summary>
internal sealed class PersonenAuskunft(PersonenLeseDbContext db, IVerschluesselung verschluesselung) : IDatenauskunft
{
    public async Task<IReadOnlyList<AuskunftAbschnitt>> AuskunftAsync(Guid personId, CancellationToken abbruch)
    {
        var abschnitte = new List<AuskunftAbschnitt>();
        var person = await db.Personen.SingleOrDefaultAsync(p => p.Id == personId, abbruch);
        if (person is not null)
        {
            var profil = new List<AuskunftZeile>
            {
                new("Vorname", person.Vorname ?? ""),
                new("Nachname", person.Nachname ?? ""),
            };
            if (person.Geburtsdatum is { } geburtsdatum)
            {
                profil.Add(new AuskunftZeile("Geburtsdatum", AuskunftFormat.Datum(geburtsdatum)));
            }

            profil.Add(new AuskunftZeile("Angelegt am", AuskunftFormat.Datum(person.AngelegtAm)));
            abschnitte.Add(new AuskunftAbschnitt("Profil", profil));
        }

        abschnitte.Add(new AuskunftAbschnitt("Familie", await FamilieAsync(personId, abbruch)));
        abschnitte.Add(new AuskunftAbschnitt("Nachweise", await NachweiseAsync(personId, abbruch)));
        abschnitte.Add(new AuskunftAbschnitt("Einsicht-Anfragen", await EinsichtenAsync(personId, abbruch)));
        abschnitte.Add(new AuskunftAbschnitt("Erinnerungen", await ErinnerungenAsync(personId, abbruch)));
        abschnitte.Add(new AuskunftAbschnitt("Zugriffsprotokoll", await ProtokollAsync(personId, abbruch)));
        return abschnitte;
    }

    /// <summary>Anzeigetext einer Nachweisart.</summary>
    internal static string Art(NachweisArt art) => art switch
    {
        NachweisArt.Fuehrungszeugnis => "Führungszeugnis",
        NachweisArt.Juleica => "JuLeiCa",
        NachweisArt.ErsteHilfe => "Erste Hilfe",
        NachweisArt.Praeventionsschulung => "Präventionsschulung",
        _ => art.ToString(),
    };

    private async Task<List<AuskunftZeile>> FamilieAsync(Guid personId, CancellationToken abbruch)
    {
        var familieId = await db.Elternteile.Where(e => e.PersonId == personId).Select(e => (Guid?)e.FamilieId).SingleOrDefaultAsync(abbruch);
        if (familieId is not { } familie)
        {
            return [];
        }

        var elternIds = await db.Elternteile.Where(e => e.FamilieId == familie).Select(e => e.PersonId).ToListAsync(abbruch);
        var eltern = await db.Personen.Where(p => elternIds.Contains(p.Id)).OrderBy(p => p.Nachname).ThenBy(p => p.Vorname).ToListAsync(abbruch);
        var kinder = await db.Personen.Where(p => p.FamilieId == familie).OrderBy(p => p.Geburtsdatum).ThenBy(p => p.Vorname).ToListAsync(abbruch);
        return
        [
            .. eltern.Select(p => new AuskunftZeile("Elternteil", AuskunftFormat.Name(p.Vorname, p.Nachname))),
            .. kinder.Select(p => new AuskunftZeile(
                "Kind",
                p.Geburtsdatum is { } tag ? $"{AuskunftFormat.Name(p.Vorname, p.Nachname)}, geboren am {AuskunftFormat.Datum(tag)}" : AuskunftFormat.Name(p.Vorname, p.Nachname))),
        ];
    }

    private async Task<List<AuskunftZeile>> NachweiseAsync(Guid personId, CancellationToken abbruch)
    {
        var nachweise = await db.Nachweise.Where(n => n.PersonId == personId).OrderBy(n => n.Art).ToListAsync(abbruch);
        return
        [
            .. nachweise.Select(n => new AuskunftZeile(
                Art(n.Art),
                n.HochgeladenAm is { } am
                    ? $"ausgestellt am {AuskunftFormat.Datum(n.Ausstellungsdatum)}, hochgeladen am {AuskunftFormat.Datum(am)}"
                    : $"ausgestellt am {AuskunftFormat.Datum(n.Ausstellungsdatum)}, ohne Datei")),
        ];
    }

    private async Task<List<AuskunftZeile>> EinsichtenAsync(Guid personId, CancellationToken abbruch)
    {
        var einsichten = await db.Einsichten.IgnoreQueryFilters().Include(e => e.Posten)
            .Where(e => e.PersonId == personId)
            .OrderBy(e => e.AngefragtAm)
            .ToListAsync(abbruch);
        var pruefende = einsichten.Where(e => e.PruefendePersonId is not null).Select(e => e.PruefendePersonId!.Value).Distinct().ToList();
        var namen = await db.Personen.Where(p => pruefende.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => AuskunftFormat.Name(p.Vorname, p.Nachname), abbruch);
        var nachweise = await db.Nachweise.Where(n => n.PersonId == personId).ToDictionaryAsync(n => n.Id, abbruch);

        var zeilen = new List<AuskunftZeile>();
        foreach (var einsicht in einsichten)
        {
            var teile = new List<string> { $"Status: {Status(einsicht.Status)}" };
            if (einsicht.PruefendePersonId is { } pruefend)
            {
                teile.Add($"prüfende Person: {namen.GetValueOrDefault(pruefend) ?? "Gelöschte Person"}");
            }

            foreach (var posten in einsicht.Posten.Where(p => p.Ergebnis == PostenErgebnisArt.Abgelehnt))
            {
                if (nachweise.GetValueOrDefault(posten.NachweisId)?.Ablehnungshinweis is { } hinweis)
                {
                    teile.Add($"Hinweis zu {Art(posten.Art)}: {await verschluesselung.EntschluesselnAsync(hinweis, abbruch)}");
                }
            }

            zeilen.Add(new AuskunftZeile($"Angefragt am {AuskunftFormat.Datum(einsicht.AngefragtAm)}", string.Join(", ", teile)));
        }

        return zeilen;
    }

    private async Task<List<AuskunftZeile>> ErinnerungenAsync(Guid personId, CancellationToken abbruch)
    {
        var erinnerungen = await db.Erinnerungen.IgnoreQueryFilters().Where(e => e.PersonId == personId).OrderBy(e => e.ErinnertAm).ToListAsync(abbruch);
        var vonIds = erinnerungen.Select(e => e.ErinnertVon).Distinct().ToList();
        var namen = await db.Personen.Where(p => vonIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => AuskunftFormat.Name(p.Vorname, p.Nachname), abbruch);
        return
        [
            .. erinnerungen.Select(e => new AuskunftZeile(
                $"Erinnert am {AuskunftFormat.Datum(e.ErinnertAm)}",
                $"von {namen.GetValueOrDefault(e.ErinnertVon) ?? "Gelöschte Person"}")),
        ];
    }

    private async Task<List<AuskunftZeile>> ProtokollAsync(Guid personId, CancellationToken abbruch)
    {
        var eintraege = await db.EinsichtProtokoll.Where(e => e.BetroffenePersonId == personId).OrderBy(e => e.Am).ToListAsync(abbruch);
        var lesendeIds = eintraege.Select(e => e.LesendePersonId).Distinct().ToList();
        var namen = await db.Personen.Where(p => lesendeIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => AuskunftFormat.Name(p.Vorname, p.Nachname), abbruch);
        return
        [
            .. eintraege.Select(e => new AuskunftZeile(
                AuskunftFormat.Datum(e.Am),
                $"{namen.GetValueOrDefault(e.LesendePersonId) ?? "Gelöschte Person"}: {Zweck(e.Zweck)}")),
        ];
    }

    private static string Status(EinsichtStatus status) => status switch
    {
        EinsichtStatus.Angefragt => "angefragt",
        EinsichtStatus.Freigegeben => "freigegeben",
        EinsichtStatus.Abgeschlossen => "abgeschlossen",
        _ => status.ToString(),
    };

    private static string Zweck(EinsichtZweck zweck) => zweck switch
    {
        EinsichtZweck.NachweiseEingesehen => "Nachweise eingesehen",
        _ => zweck.ToString(),
    };
}
