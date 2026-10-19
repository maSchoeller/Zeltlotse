using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Personen.Daten;
using Zeltlotse.Personen.Nachweise;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Personen.Zugriffsprotokoll;

/// <summary>Wofür jemand Einsicht hatte (AD-8); gespeichert als Zahl, nur anhängen. Gleiche Werte wie <c>ZugriffZweck</c> der API.</summary>
internal enum EinsichtZweck : short
{
    NachweiseEingesehen = 1,

    /// <summary>Story 2-5: Leitung oder Team hat den Freizeitpass im Freizeit-Kontext gesehen.</summary>
    FreizeitpassEingesehen = 2,

    /// <summary>Story 4-2: Der Allergen-Abgleich der Küche hat die Allergie-Auswahl gelesen, ohne lesende Person (AD-8, Zweck ohne Menschen).</summary>
    AllergeneFuerKueche = 3,
}

/// <summary>
/// Ein Eintrag im Einsicht-Protokoll (AD-8, FR-13): wer (<see cref="LesendePersonId"/>) wann wessen Daten
/// (<see cref="BetroffenePersonId"/>) wofür eingesehen hat, mit Organisation und Freizeit. Höchstens ein Eintrag je lesender Person,
/// betroffener Person, Kalendertag (Europe/Berlin) und Zweck (eindeutiger Index, ohne lesende Person gilt <c>null</c> als ein Wert). Nicht
/// organisationseigen: Das Protokoll gehört der betroffenen Person. Ist die lesende Person gelöscht, steht dort der Platzhalter. Story 4-2: Ein
/// Lesen ohne Menschen (Allergen-Abgleich der Küche) hat keine lesende Person (<c>null</c>).
/// </summary>
internal sealed class EinsichtProtokollEintrag
{
    private EinsichtProtokollEintrag()
    {
    }

    public Guid Id { get; private init; }

    public Guid BetroffenePersonId { get; private init; }

    public Guid? LesendePersonId { get; private init; }

    public Guid? OrganisationId { get; private init; }

    public Guid? FreizeitId { get; private init; }

    public EinsichtZweck Zweck { get; private init; }

    public DateTimeOffset Am { get; private init; }

    /// <summary>Der Kalendertag von <see cref="Am"/> in Europe/Berlin; Teil des eindeutigen Index.</summary>
    public DateOnly Tag { get; private init; }
}

/// <summary>
/// Schreibt einen Eintrag ins Einsicht-Protokoll (AD-8). Interner Command, kein <see cref="IBenutzerCommand"/>: Er ist die Folge
/// eines Lesezugriffs und läuft auch nach dem Abschluss einer Freizeit. Gibt es für (lesend, betroffen, Tag, Zweck) schon einen
/// Eintrag, gilt das als Erfolg (<c>ON CONFLICT DO NOTHING</c>).
/// </summary>
internal sealed record EinsichtProtokollieren(Guid BetroffenePersonId, Guid? LesendePersonId, Guid? OrganisationId, Guid? FreizeitId, EinsichtZweck Zweck)
    : ICommand, IModulCommand<PersonenDbContext>;

internal sealed class EinsichtProtokollierenHandler(PersonenDbContext db, TimeProvider uhr) : ICommandHandler<EinsichtProtokollieren>
{
    public async ValueTask<Unit> Handle(EinsichtProtokollieren command, CancellationToken cancellationToken)
    {
        var jetzt = uhr.GetUtcNow();
        var tag = NachweisRegeln.Tag(jetzt);
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO personen.einsicht_protokoll ("Id", "BetroffenePersonId", "LesendePersonId", "OrganisationId", "FreizeitId", "Zweck", "Am", "Tag")
            VALUES ({Kennung.Neu(uhr)}, {command.BetroffenePersonId}, {command.LesendePersonId}, {command.OrganisationId}, {command.FreizeitId}, {(short)command.Zweck}, {jetzt}, {tag})
            ON CONFLICT DO NOTHING
            """,
            cancellationToken);
        return Unit.Value;
    }
}
