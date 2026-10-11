using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Personen.Daten;
using Zeltlotse.Personen.Profil;
using Zeltlotse.Personen.Schluessel;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Datenschutz;

namespace Zeltlotse.Personen.Familien;

/// <summary>
/// Legt ein Kind unter 18 im Familienkonto der anfragenden Person an (FR-3). Fehlt die Familie, entsteht sie mit der Person als
/// erstem Elternteil. Kind und Personen-Schlüssel (AD-14) entstehen in derselben Transaktion. Pflicht sind nur Vorname und
/// Geburtsdatum (NFR-12); ab 18 legen Kinder ein eigenes Konto an.
/// </summary>
internal sealed record KindAnlegen(Guid ElternPersonId, string? Vorname, string? Nachname, DateOnly? Geburtsdatum)
    : ICommand<Guid>, IModulCommand<PersonenDbContext>, IBenutzerCommand
{
    /// <summary>Keine Namen oder Geburtsdaten ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(KindAnlegen)}({ElternPersonId})";
}

internal sealed class KindAnlegenValidator : AbstractValidator<KindAnlegen>
{
    public KindAnlegenValidator(TimeProvider uhr)
    {
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (KindRegeln.Vorname(c.Vorname) is { } vorname)
            {
                kontext.AddFailure(new ValidationFailure(nameof(KindAnlegen.Vorname), vorname));
            }

            if (KindRegeln.Nachname(c.Nachname) is { } nachname)
            {
                kontext.AddFailure(new ValidationFailure(nameof(KindAnlegen.Nachname), nachname));
            }

            if (KindRegeln.Geburtsdatum(c.Geburtsdatum, Volljaehrigkeit.Heute(uhr), mitVolljaehrigkeit: true) is { } geburtsdatum)
            {
                kontext.AddFailure(new ValidationFailure(nameof(KindAnlegen.Geburtsdatum), geburtsdatum));
            }
        });
    }
}

internal sealed class KindAnlegenHandler(PersonenDbContext db, Hauptschluessel hauptschluessel, TimeProvider uhr) : ICommandHandler<KindAnlegen, Guid>
{
    public async ValueTask<Guid> Handle(KindAnlegen command, CancellationToken cancellationToken)
    {
        // Zwei gleichzeitige erste Kinder derselben Person legen nur eine Familie an.
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({command.ElternPersonId.ToString()}, 0))", cancellationToken);
        var (familieId, _) = await FamilienSperre.SicherstellenAsync(db.Familien, db.Elternteile, command.ElternPersonId, uhr, cancellationToken);

        var id = Kennung.Neu(uhr);
        db.Personen.Add(Person.Kind(id, familieId, command.Vorname!, command.Nachname, command.Geburtsdatum!.Value, uhr.GetUtcNow()));
        db.PersonenSchluessel.Add(PersonenSchluessel.Anlegen(id, hauptschluessel.NeuenPersonenSchluesselUmhuellen(id), uhr));
        return id;
    }
}
