using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Personen.Daten;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Personen.Nachweise;

/// <summary>
/// Ändert genau ein Feld eines eigenen Nachweises (AD-19), heute nur das Ausstellungsdatum. Ein fremder Nachweis ist
/// <see cref="NachweisAenderung.NichtGefunden"/> (<c>404</c>). Steht der Nachweis in einem offenen Posten einer Einsicht mit
/// gültiger Freigabe, ist er <see cref="NachweisAenderung.InPruefung"/> (<c>409</c>), damit die prüfende Person genau bestätigt,
/// was sie gesehen hat. Ein geändertes Datum löscht den Ablehnungshinweis.
/// </summary>
internal sealed record NachweisFeldAendern(Guid PersonId, Guid NachweisId, NachweisFeld Feld, uint Version, string? AlterWert, string? NeuerWert)
    : ICommand<NachweisAenderung>, IModulCommand<PersonenDbContext>, IBenutzerCommand
{
    public override string ToString() => $"{nameof(NachweisFeldAendern)}({NachweisId}, {Feld})";
}

/// <summary>Ergebnis einer Feldänderung; bei <see cref="Gespeichert"/> der verfolgte Nachweis.</summary>
internal abstract record NachweisAenderung
{
    private NachweisAenderung()
    {
    }

    public sealed record Gespeichert(Nachweis Nachweis) : NachweisAenderung;

    public sealed record Konflikt : NachweisAenderung;

    public sealed record NichtGefunden : NachweisAenderung;

    /// <summary>Gerade zur Prüfung freigegeben: <c>409</c>.</summary>
    public sealed record InPruefung : NachweisAenderung;
}

internal sealed class NachweisFeldAendernValidator : AbstractValidator<NachweisFeldAendern>
{
    public NachweisFeldAendernValidator(TimeProvider uhr)
    {
        RuleFor(c => c).Custom((c, kontext) =>
        {
            var fehler = c.Feld switch
            {
                NachweisFeld.Ausstellungsdatum => NachweisRegeln.Ausstellungsdatum(c.NeuerWert, NachweisRegeln.Heute(uhr)),
                _ => "Unbekanntes Feld.",
            };
            if (fehler is not null)
            {
                kontext.AddFailure(new ValidationFailure(Enum.IsDefined(c.Feld) ? NachweisFelder.JsonName(c.Feld) : "feld", fehler));
            }
        });
    }
}

internal sealed class NachweisFeldAendernHandler(PersonenDbContext db, TimeProvider uhr) : ICommandHandler<NachweisFeldAendern, NachweisAenderung>
{
    public async ValueTask<NachweisAenderung> Handle(NachweisFeldAendern command, CancellationToken cancellationToken)
    {
        // Zeilensperre wie beim Profil: Gleichzeitige Änderungen desselben Nachweises laufen nacheinander.
        var nachweis = await NachweisSperre.EigenerAsync(db.Nachweise, command.PersonId, command.NachweisId, cancellationToken);
        if (nachweis is null)
        {
            return new NachweisAenderung.NichtGefunden();
        }

        if (await NachweisSperre.InPruefungAsync(db.EinsichtPosten, db.Einsichten, nachweis.Id, uhr.GetUtcNow(), cancellationToken))
        {
            return new NachweisAenderung.InPruefung();
        }

        return nachweis.FeldAendern(command.Feld, command.AlterWert, command.NeuerWert!)
            ? new NachweisAenderung.Gespeichert(nachweis)
            : new NachweisAenderung.Konflikt();
    }
}

/// <summary>Lädt Zeilen der Nachweis-Commands mit Zeilensperre (<c>FOR UPDATE</c>) in der Transaktion der Pipeline.</summary>
internal static class NachweisSperre
{
    /// <summary>Der Nachweis unter Zeilensperre, nur wenn er der Person gehört.</summary>
    public static async Task<Nachweis?> EigenerAsync(DbSet<Nachweis> nachweise, Guid personId, Guid nachweisId, CancellationToken abbruch) =>
        await NachweisAsync(nachweise, nachweisId, abbruch) is { } nachweis && nachweis.PersonId == personId ? nachweis : null;

    public static async Task<Nachweis?> NachweisAsync(DbSet<Nachweis> nachweise, Guid nachweisId, CancellationToken abbruch) =>
        (await nachweise
            .FromSql($"SELECT *, xmin FROM personen.nachweis WHERE \"Id\" = {nachweisId} FOR UPDATE")
            .ToListAsync(abbruch))
        .SingleOrDefault();

    /// <summary>
    /// Steht der Nachweis in einem offenen Posten einer Einsicht mit gültiger Freigabe (gleich welcher Organisation)? Dann darf
    /// die Person Datum und Datei nicht ändern, bis entschieden ist oder die Freigabe abläuft.
    /// </summary>
    public static Task<bool> InPruefungAsync(
        DbSet<NachweisEinsichtPosten> posten, DbSet<NachweisEinsicht> einsichten, Guid nachweisId, DateTimeOffset jetzt, CancellationToken abbruch) =>
        posten
            .Where(p => p.NachweisId == nachweisId && p.Ergebnis == PostenErgebnisArt.Offen)
            .Join(einsichten.IgnoreQueryFilters(), p => p.EinsichtId, e => e.Id, (p, e) => e)
            .AnyAsync(e => e.Status == EinsichtStatus.Freigegeben && e.FreigegebenBis >= jetzt, abbruch);

    /// <summary>
    /// Die Einsicht unter Zeilensperre mit ihren Posten. Mit <paramref name="ohneMandant"/> auch ohne Mandanten-Kontext (nur der
    /// personenbezogene Weg der Person selbst, AD-26); sonst gilt der Mandantenfilter.
    /// </summary>
    public static async Task<NachweisEinsicht?> EinsichtAsync(
        DbSet<NachweisEinsicht> einsichten, DbSet<NachweisEinsichtPosten> posten, Guid einsichtId, CancellationToken abbruch, bool ohneMandant = false)
    {
        var abfrage = einsichten.FromSql($"SELECT *, xmin FROM personen.nachweis_einsicht WHERE \"Id\" = {einsichtId} FOR UPDATE");
        var einsicht = (await (ohneMandant ? abfrage.IgnoreQueryFilters() : abfrage).ToListAsync(abbruch)).SingleOrDefault();
        if (einsicht is not null)
        {
            // Füllt die Posten-Liste der verfolgten Einsicht.
            await posten.Where(p => p.EinsichtId == einsicht.Id).LoadAsync(abbruch);
        }

        return einsicht;
    }
}
