using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Personen.Contracts.Anmeldung;
using Zeltlotse.Personen.Contracts.Freizeitpass;
using Zeltlotse.Personen.Daten;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;

namespace Zeltlotse.Personen.Freizeitpass;

/// <summary>
/// Ändert genau ein Feld einer Antwort im Freizeitpass (AD-19, FR-23): die Person selbst oder ein Elternteil der Familie
/// (<see cref="IVertretung.DarfAnmeldenAsync"/>), sonst <see cref="AntwortAenderung.NichtGefunden"/>. Gibt es die Antwort noch nicht,
/// legt der erste PATCH sie an; der alte Wert muss dann leer sein. Konfliktregel: Weicht der aktuelle Wert desselben Felds vom
/// mitgeschickten alten Wert ab, ist das Ergebnis <see cref="AntwortAenderung.Konflikt"/>. Die Kennung prüft der Endpunkt.
/// </summary>
internal sealed record FreizeitpassAntwortAendern(Guid AngemeldetePersonId, Guid PersonId, string Kennung, FreizeitpassFeld Feld, string? AlterWert, string? NeuerWert)
    : ICommand<AntwortAenderung>, IModulCommand<PersonenDbContext>, IBenutzerCommand
{
    /// <summary>Keine Antworttexte ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(FreizeitpassAntwortAendern)}({PersonId}, {Kennung}, {Feld})";
}

/// <summary>Ergebnis einer Feldänderung; bei <see cref="Gespeichert"/> die Antwort (nach dem Speichern mit neuer Version) und der gespeicherte Wert.</summary>
internal abstract record AntwortAenderung
{
    private AntwortAenderung()
    {
    }

    /// <summary>Gespeichert; <paramref name="Antwort"/> ist <c>null</c>, wenn eine noch nicht vorhandene Antwort leer bleibt (Version 0).</summary>
    public sealed record Gespeichert(FreizeitpassAntwort? Antwort, string? Wert) : AntwortAenderung
    {
        /// <summary>Keine Antworttexte ausgeben (AD-22).</summary>
        public override string ToString() => nameof(Gespeichert);
    }

    public sealed record Konflikt : AntwortAenderung;

    public sealed record NichtGefunden : AntwortAenderung;
}

internal sealed class FreizeitpassAntwortAendernValidator : AbstractValidator<FreizeitpassAntwortAendern>
{
    public FreizeitpassAntwortAendernValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (!Enum.IsDefined(c.Feld) || Fragenkatalog.Finden(c.Kennung) is not { } frage)
            {
                kontext.AddFailure(new ValidationFailure("feld", "Unbekanntes Feld."));
                return;
            }

            if (FreizeitpassRegeln.Pruefen(frage, c.Feld, c.NeuerWert).Fehler is { } fehler)
            {
                kontext.AddFailure(new ValidationFailure(FreizeitpassFelder.JsonName(c.Feld), fehler));
            }
        });
}

internal sealed class FreizeitpassAntwortAendernHandler(PersonenDbContext db, IVertretung vertretung, IVerschluesselung verschluesselung, TimeProvider uhr)
    : ICommandHandler<FreizeitpassAntwortAendern, AntwortAenderung>
{
    public async ValueTask<AntwortAenderung> Handle(FreizeitpassAntwortAendern command, CancellationToken cancellationToken)
    {
        if (!await FreizeitpassRegeln.DarfAsync(db.Personen, vertretung, command.AngemeldetePersonId, command.PersonId, cancellationToken))
        {
            return new AntwortAenderung.NichtGefunden();
        }

        var frage = Fragenkatalog.Finden(command.Kennung) ?? throw new InvalidOperationException("Unbekannte Kennung.");
        var antwort = await FreizeitpassSperre.AntwortAsync(db.Database, db.FreizeitpassAntworten, command.PersonId, command.Kennung, cancellationToken);
        var aktuell = antwort is null ? null : await FreizeitpassRegeln.TextAsync(verschluesselung, Feld(antwort, command.Feld), cancellationToken);
        if (!string.Equals(aktuell, FreizeitpassRegeln.Vergleichswert(frage, command.Feld, command.AlterWert), StringComparison.Ordinal))
        {
            return new AntwortAenderung.Konflikt();
        }

        var neu = FreizeitpassRegeln.Pruefen(frage, command.Feld, command.NeuerWert).Wert;
        if (string.Equals(aktuell, neu, StringComparison.Ordinal))
        {
            return new AntwortAenderung.Gespeichert(antwort, aktuell);
        }

        var jetzt = uhr.GetUtcNow();
        if (antwort is null)
        {
            antwort = FreizeitpassAntwort.Anlegen(Kennung.Neu(uhr), command.PersonId, command.Kennung, jetzt);
            db.FreizeitpassAntworten.Add(antwort);
        }

        antwort.Setzen(command.Feld, neu is null ? null : await verschluesselung.VerschluesselnAsync(command.PersonId, neu, cancellationToken), jetzt);
        return new AntwortAenderung.Gespeichert(antwort, neu);
    }

    private static Geschuetzt? Feld(FreizeitpassAntwort antwort, FreizeitpassFeld feld) =>
        feld == FreizeitpassFeld.Wert ? antwort.Wert : antwort.Sonstiges;
}

/// <summary>
/// „Stimmt“ (FR-23): bestätigt eine vorhandene Antwort neu; ohne Antwort oder ohne Recht <c>false</c> (<c>404</c>).
/// </summary>
internal sealed record FreizeitpassAntwortBestaetigen(Guid AngemeldetePersonId, Guid PersonId, string Kennung)
    : ICommand<bool>, IModulCommand<PersonenDbContext>, IBenutzerCommand;

internal sealed class FreizeitpassAntwortBestaetigenHandler(PersonenDbContext db, IVertretung vertretung, TimeProvider uhr)
    : ICommandHandler<FreizeitpassAntwortBestaetigen, bool>
{
    public async ValueTask<bool> Handle(FreizeitpassAntwortBestaetigen command, CancellationToken cancellationToken)
    {
        if (!await FreizeitpassRegeln.DarfAsync(db.Personen, vertretung, command.AngemeldetePersonId, command.PersonId, cancellationToken))
        {
            return false;
        }

        var antwort = await FreizeitpassSperre.AntwortAsync(db.Database, db.FreizeitpassAntworten, command.PersonId, command.Kennung, cancellationToken);
        if (antwort is not { Beantwortet: true })
        {
            return false;
        }

        antwort.Bestaetigen(uhr.GetUtcNow());
        return true;
    }
}

/// <summary>
/// Gast-Link (AD-11, FR-17): speichert das Formular des Freizeitpasses für ein Kind der Gast-Familie in einer Transaktion. Nur
/// geänderte Werte werden geschrieben (neues <c>BestaetigtAm</c>); eine unveränderte Antwort wird nur bestätigt, wenn ihre Kennung
/// in <see cref="Bestaetigt"/> steht. Letzter Schreiber gewinnt. Ungültiges Token oder fremdes Kind: <c>false</c> (<c>404</c>) und
/// <c>LinkEinloesungFehlgeschlagen</c> im Sicherheitsprotokoll.
/// </summary>
internal sealed record FreizeitpassFormularSpeichern(string? GastToken, Guid PersonId, IReadOnlyList<FreizeitpassFormularAntwort> Antworten, IReadOnlyList<string> Bestaetigt)
    : ICommand<bool>, IModulCommand<PersonenDbContext>, IBenutzerCommand
{
    /// <summary>Nie das Token, keine Antworttexte ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(FreizeitpassFormularSpeichern)}({PersonId})";
}

internal sealed class FreizeitpassFormularSpeichernValidator : AbstractValidator<FreizeitpassFormularSpeichern>
{
    public FreizeitpassFormularSpeichernValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            var gesehen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var antwort in c.Antworten ?? [])
            {
                if (antwort is null || Fragenkatalog.Finden(antwort.Kennung) is not { } frage || !gesehen.Add(antwort.Kennung))
                {
                    kontext.AddFailure(new ValidationFailure("antworten", "Unbekannte oder doppelte Frage."));
                    continue;
                }

                foreach (var (feld, wert) in new[] { (FreizeitpassFeld.Wert, antwort.Wert), (FreizeitpassFeld.Sonstiges, antwort.Sonstiges) })
                {
                    if (FreizeitpassRegeln.Pruefen(frage, feld, wert).Fehler is { } fehler)
                    {
                        kontext.AddFailure(new ValidationFailure(FreizeitpassFelder.FormularFehler(antwort.Kennung, feld), fehler));
                    }
                }
            }
        });
}

internal sealed class FreizeitpassFormularSpeichernHandler(
    PersonenDbContext db, IGastZugaenge gaeste, IVerschluesselung verschluesselung, ISicherheitsprotokoll protokoll, TimeProvider uhr)
    : ICommandHandler<FreizeitpassFormularSpeichern, bool>
{
    public async ValueTask<bool> Handle(FreizeitpassFormularSpeichern command, CancellationToken cancellationToken)
    {
        var gast = await gaeste.PruefenAsync(command.GastToken, cancellationToken);
        if (gast is null || !gast.KindPersonIds.Contains(command.PersonId))
        {
            await protokoll.SchreibenAsync(SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen, gast?.ElternPersonId, abbruch: cancellationToken);
            return false;
        }

        var jetzt = uhr.GetUtcNow();
        var bestaetigt = command.Bestaetigt.ToHashSet(StringComparer.Ordinal);
        foreach (var eingabe in command.Antworten)
        {
            var frage = Fragenkatalog.Finden(eingabe.Kennung)!;
            var antwort = await FreizeitpassSperre.AntwortAsync(db.Database, db.FreizeitpassAntworten, command.PersonId, frage.Kennung, cancellationToken);
            var wert = FreizeitpassRegeln.Pruefen(frage, FreizeitpassFeld.Wert, eingabe.Wert).Wert;
            var sonstiges = FreizeitpassRegeln.Pruefen(frage, FreizeitpassFeld.Sonstiges, eingabe.Sonstiges).Wert;
            var alterWert = antwort is null ? null : await FreizeitpassRegeln.TextAsync(verschluesselung, antwort.Wert, cancellationToken);
            var altesSonstiges = antwort is null ? null : await FreizeitpassRegeln.TextAsync(verschluesselung, antwort.Sonstiges, cancellationToken);
            var geaendert = !string.Equals(wert, alterWert, StringComparison.Ordinal) || !string.Equals(sonstiges, altesSonstiges, StringComparison.Ordinal);
            if (geaendert)
            {
                if (antwort is null)
                {
                    antwort = FreizeitpassAntwort.Anlegen(Kennung.Neu(uhr), command.PersonId, frage.Kennung, jetzt);
                    db.FreizeitpassAntworten.Add(antwort);
                }

                antwort.Setzen(FreizeitpassFeld.Wert, wert is null ? null : await verschluesselung.VerschluesselnAsync(command.PersonId, wert, cancellationToken), jetzt);
                antwort.Setzen(FreizeitpassFeld.Sonstiges, sonstiges is null ? null : await verschluesselung.VerschluesselnAsync(command.PersonId, sonstiges, cancellationToken), jetzt);
            }
            else if (antwort is { Beantwortet: true } && bestaetigt.Contains(frage.Kennung))
            {
                antwort.Bestaetigen(jetzt);
            }
        }

        return true;
    }
}

/// <summary>Gemeinsame Regeln der Freizeitpass-Commands und -Endpunkte.</summary>
internal static class FreizeitpassRegeln
{
    /// <summary>Prüft und normalisiert den Wert eines Felds nach dem Antworttyp der Frage.</summary>
    public static Pruefung Pruefen(Katalogfrage frage, FreizeitpassFeld feld, string? wert) => feld switch
    {
        FreizeitpassFeld.Wert => Fragenkatalog.WertPruefen(frage, wert),
        FreizeitpassFeld.Sonstiges => Fragenkatalog.SonstigesPruefen(frage, wert),
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };

    /// <summary>Der mitgeschickte alte Wert in normalisierter Form (ungültig: getrimmt, wie gesendet), damit der Vergleich stabil ist.</summary>
    public static string? Vergleichswert(Katalogfrage frage, FreizeitpassFeld feld, string? alterWert)
    {
        var pruefung = Pruefen(frage, feld, alterWert);
        return pruefung.Gueltig ? pruefung.Wert : alterWert?.Trim();
    }

    public static async Task<string?> TextAsync(IVerschluesselung verschluesselung, Geschuetzt? geschuetzt, CancellationToken abbruch) =>
        geschuetzt is null ? null : await verschluesselung.EntschluesselnAsync(geschuetzt, abbruch);

    /// <summary>Die Person existiert, und die angemeldete Person ist sie selbst oder Elternteil der Familie (Kind unter 18).</summary>
    public static async Task<bool> DarfAsync(DbSet<Profil.Person> personen, IVertretung vertretung, Guid angemeldet, Guid personId, CancellationToken abbruch) =>
        await vertretung.DarfAnmeldenAsync(angemeldet, personId, abbruch) && await personen.AnyAsync(p => p.Id == personId, abbruch);
}

/// <summary>Lädt eine Antwort unter Sperre in der Transaktion der Pipeline.</summary>
internal static class FreizeitpassSperre
{
    /// <summary>
    /// Sperre je (Person, Kennung) auch dann, wenn es die Zeile noch nicht gibt (zwei erste PATCHs laufen nacheinander), dann die Zeile
    /// mit <c>FOR UPDATE</c>.
    /// </summary>
    public static async Task<FreizeitpassAntwort?> AntwortAsync(
        DatabaseFacade datenbank, DbSet<FreizeitpassAntwort> antworten, Guid personId, string kennung, CancellationToken abbruch)
    {
        await datenbank.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"freizeitpass:{personId}:{kennung}"}, 0))", abbruch);
        return (await antworten
                .FromSql($"SELECT *, xmin FROM personen.freizeitpass_antwort WHERE \"PersonId\" = {personId} AND \"Kennung\" = {kennung} FOR UPDATE")
                .ToListAsync(abbruch))
            .SingleOrDefault();
    }
}
