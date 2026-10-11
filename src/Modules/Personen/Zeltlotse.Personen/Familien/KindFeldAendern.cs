using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Personen.Daten;
using Zeltlotse.Personen.Profil;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Personen.Familien;

/// <summary>
/// Ändert genau ein Feld eines Kinderprofils (AD-19), nur durch einen Elternteil der Familie des Kindes. Es gelten die
/// Feldregeln von <see cref="KindAnlegen"/> außer der 18-Grenze: Nach einer Änderung darf ein Kind volljährig sein.
/// </summary>
internal sealed record KindFeldAendern(Guid ElternPersonId, Guid KindId, KindFeld Feld, uint Version, string? AlterWert, string? NeuerWert)
    : ICommand<KindAenderung>, IModulCommand<PersonenDbContext>, IBenutzerCommand
{
    public override string ToString() => $"{nameof(KindFeldAendern)}({KindId}, {Feld})";
}

/// <summary>Ergebnis einer Feldänderung am Kinderprofil; bei <see cref="Gespeichert"/> die verfolgte Person.</summary>
internal abstract record KindAenderung
{
    private KindAenderung()
    {
    }

    public sealed record Gespeichert(Person Person) : KindAenderung;

    public sealed record Konflikt : KindAenderung;

    /// <summary>Kein Kind der eigenen Familie (oder schon übergeben): <c>404</c>.</summary>
    public sealed record NichtGefunden : KindAenderung;
}

internal sealed class KindFeldAendernValidator : AbstractValidator<KindFeldAendern>
{
    public KindFeldAendernValidator(TimeProvider uhr)
    {
        RuleFor(c => c).Custom((c, kontext) =>
        {
            var fehler = c.Feld switch
            {
                KindFeld.Vorname => KindRegeln.Vorname(c.NeuerWert),
                KindFeld.Nachname => KindRegeln.Nachname(c.NeuerWert),
                KindFeld.Geburtsdatum => KindRegeln.Geburtsdatum(c.NeuerWert, Volljaehrigkeit.Heute(uhr)),
                _ => "Unbekanntes Feld.",
            };
            if (fehler is not null)
            {
                kontext.AddFailure(new ValidationFailure(Enum.IsDefined(c.Feld) ? KindFelder.JsonName(c.Feld) : "feld", fehler));
            }
        });
    }
}

internal sealed class KindFeldAendernHandler(PersonenDbContext db) : ICommandHandler<KindFeldAendern, KindAenderung>
{
    public async ValueTask<KindAenderung> Handle(KindFeldAendern command, CancellationToken cancellationToken)
    {
        var elternteil = await db.Elternteile.SingleOrDefaultAsync(e => e.PersonId == command.ElternPersonId, cancellationToken);
        // Zeilensperre wie beim eigenen Profil: Gleichzeitige Änderungen desselben Kindes laufen nacheinander.
        var kind = elternteil is null ? null : await FamilienSperre.KindAsync(db.Personen, command.KindId, elternteil.FamilieId, cancellationToken);
        if (kind is null)
        {
            return new KindAenderung.NichtGefunden();
        }

        return kind.KindFeldAendern(command.Feld, command.AlterWert, command.NeuerWert)
            ? new KindAenderung.Gespeichert(kind)
            : new KindAenderung.Konflikt();
    }
}
