using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Personen.Daten;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Personen.Profil;

/// <summary>
/// Ändert genau ein Feld des eigenen Profils (AD-19). Konfliktregel: Weicht der aktuelle Wert desselben Felds vom
/// mitgeschickten alten Wert ab, ist das Ergebnis <see cref="ProfilAenderung.Konflikt"/>; Änderungen an anderen Feldern
/// seit der mitgeschickten Version stören nicht.
/// </summary>
internal sealed record ProfilFeldAendern(Guid PersonId, ProfilFeld Feld, uint Version, string? AlterWert, string? NeuerWert)
    : ICommand<ProfilAenderung>, IModulCommand<PersonenDbContext>, IBenutzerCommand
{
    public override string ToString() => $"{nameof(ProfilFeldAendern)}({PersonId}, {Feld})";
}

/// <summary>Ergebnis einer Feldänderung; bei <see cref="Gespeichert"/> die verfolgte Person (nach dem Speichern mit neuer Version).</summary>
internal abstract record ProfilAenderung
{
    private ProfilAenderung()
    {
    }

    public sealed record Gespeichert(Person Person) : ProfilAenderung;

    public sealed record Konflikt : ProfilAenderung;

    public sealed record PersonFehlt : ProfilAenderung;
}

internal sealed class ProfilFeldAendernValidator : AbstractValidator<ProfilFeldAendern>
{
    public ProfilFeldAendernValidator()
    {
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (!Enum.IsDefined(c.Feld))
            {
                kontext.AddFailure(new ValidationFailure("feld", "Unbekanntes Feld."));
            }
            else if (c.NeuerWert is { } wert && wert.Trim().Length > ProfilFelder.Laenge(c.Feld))
            {
                kontext.AddFailure(new ValidationFailure(
                    ProfilFelder.JsonName(c.Feld), $"Höchstens {ProfilFelder.Laenge(c.Feld)} Zeichen."));
            }
        });
    }
}

internal sealed class ProfilFeldAendernHandler(PersonenDbContext db) : ICommandHandler<ProfilFeldAendern, ProfilAenderung>
{
    public async ValueTask<ProfilAenderung> Handle(ProfilFeldAendern command, CancellationToken cancellationToken)
    {
        // Zeilensperre (FOR UPDATE) in der Transaktion der Pipeline: Gleichzeitige Änderungen derselben Person laufen
        // nacheinander, die zweite sieht den gespeicherten Stand samt neuer Version, und allein die Regel zum alten Wert entscheidet.
        var person = (await db.Personen
                .FromSql($"SELECT *, xmin FROM personen.person WHERE \"Id\" = {command.PersonId} FOR UPDATE")
                .ToListAsync(cancellationToken))
            .SingleOrDefault();
        if (person is null)
        {
            return new ProfilAenderung.PersonFehlt();
        }

        return person.FeldAendern(command.Feld, command.AlterWert, command.NeuerWert)
            ? new ProfilAenderung.Gespeichert(person)
            : new ProfilAenderung.Konflikt();
    }
}
