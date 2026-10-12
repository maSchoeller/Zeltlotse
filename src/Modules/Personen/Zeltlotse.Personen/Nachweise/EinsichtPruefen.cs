using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Personen.Daten;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Datenschutz;

namespace Zeltlotse.Personen.Nachweise;

/// <summary>
/// Prüfung übernehmen (AD-9): bindet die Einsicht an genau eine prüfende Person. Ist niemand gebunden, über die Route einer
/// Freizeit (<see cref="FreizeitId"/>), in der die Person der Einsicht eine Rolle hat (die Leitung prüft der Host), oder über die
/// Org-Route (<see cref="FreizeitId"/> <c>null</c>, Org-Admin prüft der Host); nie für die eigene Einsicht. Ist die anfragende
/// Person schon gebunden, ändert sich nichts. Sonst <see cref="PruefungUebernahme.NichtGefunden"/>; ohne gültige Freigabe
/// <see cref="PruefungUebernahme.NichtFreigegeben"/>. Mandant aus der Route.
/// </summary>
internal sealed record EinsichtPruefungUebernehmen(Guid? FreizeitId, Guid AnfragendePersonId, Guid EinsichtId)
    : ICommand<PruefungUebernahme>, IModulCommand<PersonenDbContext>, IBenutzerCommand;

/// <summary>Ergebnis der Übernahme.</summary>
internal abstract record PruefungUebernahme
{
    private PruefungUebernahme()
    {
    }

    public sealed record Gebunden : PruefungUebernahme;

    /// <summary><c>404</c>: unbekannt, an jemand anderen gebunden, eigene Einsicht oder andere Freizeit.</summary>
    public sealed record NichtGefunden : PruefungUebernahme;

    /// <summary><c>409</c>: nicht freigegeben, abgelaufen oder abgeschlossen.</summary>
    public sealed record NichtFreigegeben : PruefungUebernahme;
}

internal sealed class EinsichtPruefungUebernehmenHandler(PersonenDbContext db, TimeProvider uhr)
    : ICommandHandler<EinsichtPruefungUebernehmen, PruefungUebernahme>
{
    public async ValueTask<PruefungUebernahme> Handle(EinsichtPruefungUebernehmen command, CancellationToken cancellationToken)
    {
        var einsicht = await NachweisSperre.EinsichtAsync(db.Einsichten, db.EinsichtPosten, command.EinsichtId, cancellationToken);
        if (einsicht is null
            || !await EinsichtZugang.PruefbarAsync(einsicht.PruefendePersonId, einsicht.PersonId, command.FreizeitId, command.AnfragendePersonId, db.FreizeitRollen, cancellationToken))
        {
            return new PruefungUebernahme.NichtGefunden();
        }

        if (!einsicht.FreigabeGueltig(uhr.GetUtcNow()))
        {
            return new PruefungUebernahme.NichtFreigegeben();
        }

        einsicht.PruefungUebernehmen(command.AnfragendePersonId);
        return new PruefungUebernahme.Gebunden();
    }
}

/// <summary>Wer eine Einsicht sehen und prüfen darf (AD-9); gemeinsam für Commands und Endpunkte.</summary>
internal static class EinsichtZugang
{
    /// <summary>
    /// An die anfragende Person gebunden, oder ungebunden und nicht die eigene Einsicht, wobei die Person der Einsicht bei einer
    /// Freizeit-Route eine Rolle in dieser Freizeit hat (<paramref name="rolleInRoutenFreizeit"/>). Über die Org-Route reicht
    /// „ungebunden und nicht die eigene“.
    /// </summary>
    public static bool Pruefbar(Guid? pruefend, Guid person, Guid anfragend, bool rolleInRoutenFreizeit) =>
        pruefend == anfragend || (pruefend is null && person != anfragend && rolleInRoutenFreizeit);

    /// <summary>Wie <see cref="Pruefbar"/>; die Rolle in der Freizeit der Route kommt aus der eigenen Liste (AD-8).</summary>
    public static async Task<bool> PruefbarAsync(
        Guid? pruefend, Guid person, Guid? routenFreizeit, Guid anfragend, IQueryable<Freizeitrollen.FreizeitRolle> rollen, CancellationToken abbruch)
    {
        if (pruefend == anfragend)
        {
            return true;
        }

        if (pruefend is not null || person == anfragend)
        {
            return false;
        }

        return routenFreizeit is not { } freizeit || await rollen.AnyAsync(r => r.FreizeitId == freizeit && r.PersonId == person, abbruch);
    }
}

/// <summary>
/// Bestätigt oder lehnt einen Nachweis einer Einsicht ab (FR-10, AD-9): nur die gebundene prüfende Person, nur bei gültiger
/// Freigabe und nur für einen offenen Posten, sonst <c>false</c> (<c>404</c>). Bestätigen legt <c>NachweisEingesehen</c> ab;
/// Ablehnen verlangt einen Hinweis (1–500 Zeichen) und speichert ihn verschlüsselt mit dem Schlüssel der Person am Nachweis.
/// Sind alle Posten entschieden und mindestens einer abgelehnt, entsteht im selben Schritt eine neue offene Einsicht mit
/// derselben Organisation, Freizeit und prüfenden Person, nur für die abgelehnten Arten.
/// </summary>
internal sealed record NachweisEntscheiden(Guid AnfragendePersonId, Guid EinsichtId, Guid NachweisId, bool Bestaetigt, string? Hinweis)
    : ICommand<bool>, IModulCommand<PersonenDbContext>, IBenutzerCommand
{
    /// <summary>Nie den Hinweis ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(NachweisEntscheiden)}({EinsichtId}, {NachweisId}, {Bestaetigt})";
}

internal sealed class NachweisEntscheidenValidator : AbstractValidator<NachweisEntscheiden>
{
    public NachweisEntscheidenValidator()
    {
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (!c.Bestaetigt && NachweisRegeln.Hinweis(c.Hinweis) is { } fehler)
            {
                kontext.AddFailure(new ValidationFailure(NachweisFelder.Hinweis, fehler));
            }
        });
    }
}

internal sealed class NachweisEntscheidenHandler(PersonenDbContext db, IVerschluesselung verschluesselung, TimeProvider uhr)
    : ICommandHandler<NachweisEntscheiden, bool>
{
    public async ValueTask<bool> Handle(NachweisEntscheiden command, CancellationToken cancellationToken)
    {
        var jetzt = uhr.GetUtcNow();
        var einsicht = await NachweisSperre.EinsichtAsync(db.Einsichten, db.EinsichtPosten, command.EinsichtId, cancellationToken);
        if (einsicht?.OffenerPosten(command.AnfragendePersonId, command.NachweisId, jetzt) is null)
        {
            return false;
        }

        var nachweis = await NachweisSperre.EigenerAsync(db.Nachweise, einsicht.PersonId, command.NachweisId, cancellationToken)
                       ?? throw new InvalidOperationException("Der Nachweis eines offenen Postens fehlt.");
        if (command.Bestaetigt)
        {
            einsicht.Bestaetigen(nachweis.Id, nachweis.Ausstellungsdatum, jetzt);
        }
        else
        {
            nachweis.Abgelehnt(await verschluesselung.VerschluesselnAsync(nachweis.PersonId, command.Hinweis!.Trim(), cancellationToken));
            einsicht.Ablehnen(nachweis.Id, jetzt);
        }

        if (einsicht.MitAblehnungAbgeschlossen)
        {
            // FR-10: „die Einsicht wird neu angefordert“ – von der prüfenden Person, im selben Schritt.
            await NachweisRegeln.EinsichtSperrenAsync(db, einsicht.PersonId, einsicht.OrganisationId, cancellationToken);
            db.Einsichten.Add(einsicht.NeuAnfordern(Kennung.Neu(uhr), jetzt));
        }

        return true;
    }
}
