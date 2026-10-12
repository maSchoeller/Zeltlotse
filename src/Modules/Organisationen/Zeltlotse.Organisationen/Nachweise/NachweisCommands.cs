using System.Globalization;
using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Organisationen;
using Zeltlotse.Organisationen.Daten;
using Zeltlotse.Personen.Contracts.Events;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Ereignisse;
using NachweisArt = Zeltlotse.Personen.Contracts.Events.NachweisArt;

namespace Zeltlotse.Organisationen.Nachweise;

/// <summary>
/// Ändert genau ein Feld der Nachweis-Richtlinie einer Art (AD-19, Org-Admin). Der Mandant kommt aus der Route; eine Zeile einer
/// anderen Organisation ist <see cref="RichtlinienAenderung.NichtGefunden"/>.
/// </summary>
internal sealed record RichtlinieFeldAendern(Guid OrganisationId, NachweisArt Art, RichtlinieFeld Feld, uint Version, string? AlterWert, string? NeuerWert)
    : ICommand<RichtlinienAenderung>, IModulCommand<OrganisationenDbContext>, IBenutzerCommand;

/// <summary>Ergebnis einer Feldänderung der Richtlinie; bei <see cref="Gespeichert"/> die verfolgte Zeile.</summary>
internal abstract record RichtlinienAenderung
{
    private RichtlinienAenderung()
    {
    }

    public sealed record Gespeichert(NachweisRichtlinie Richtlinie) : RichtlinienAenderung;

    public sealed record Konflikt : RichtlinienAenderung;

    public sealed record NichtGefunden : RichtlinienAenderung;
}

internal sealed class RichtlinieFeldAendernValidator : AbstractValidator<RichtlinieFeldAendern>
{
    public RichtlinieFeldAendernValidator()
    {
        RuleFor(c => c).Custom((c, kontext) =>
        {
            var fehler = c.Feld switch
            {
                RichtlinieFeld.Pflicht => c.NeuerWert?.Trim() is "true" or "false" ? null : "Pflicht ist „true“ oder „false“.",
                RichtlinieFeld.GueltigkeitMonate => int.TryParse(c.NeuerWert?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var monate)
                                                    && monate is >= RichtlinieFelder.GueltigkeitMindestens and <= RichtlinieFelder.GueltigkeitHoechstens
                    ? null
                    : $"Die Gültigkeit muss zwischen {RichtlinieFelder.GueltigkeitMindestens} und {RichtlinieFelder.GueltigkeitHoechstens} Monaten liegen.",
                _ => "Unbekanntes Feld.",
            };
            if (fehler is not null)
            {
                kontext.AddFailure(new ValidationFailure(Enum.IsDefined(c.Feld) ? RichtlinieFelder.JsonName(c.Feld) : "feld", fehler));
            }
        });
    }
}

internal sealed class RichtlinieFeldAendernHandler(OrganisationenDbContext db) : ICommandHandler<RichtlinieFeldAendern, RichtlinienAenderung>
{
    public async ValueTask<RichtlinienAenderung> Handle(RichtlinieFeldAendern command, CancellationToken cancellationToken)
    {
        // Zeilensperre (FOR UPDATE): Gleichzeitige Änderungen derselben Zeile laufen nacheinander. Der Mandantenfilter gilt.
        var zeile = (await db.Richtlinien
                .FromSql($"SELECT *, xmin FROM organisationen.nachweis_richtlinie WHERE \"OrganisationId\" = {command.OrganisationId} AND \"Art\" = {(short)command.Art} FOR UPDATE")
                .ToListAsync(cancellationToken))
            .SingleOrDefault();
        if (zeile is null)
        {
            return new RichtlinienAenderung.NichtGefunden();
        }

        return zeile.FeldAendern(command.Feld, command.AlterWert, command.NeuerWert!)
            ? new RichtlinienAenderung.Gespeichert(zeile)
            : new RichtlinienAenderung.Konflikt();
    }
}

/// <summary>
/// Empfänger von <see cref="NachweisEingesehen"/> (Tabelle Ereignis-Fluss, AD-9): speichert nur die Bestätigung mit
/// „gültig bis“ = Ausstellungsdatum + Gültigkeit der Richtlinie zum Zeitpunkt des Empfangs (fehlt die Zeile, der Vorgabewert).
/// Die jüngste Einsicht gewinnt. Mandant aus dem Umschlag; kein Benutzer-Command.
/// </summary>
internal sealed record NachweisBestaetigungSpeichern(
    Umschlag Umschlag, Guid PersonId, NachweisArt Art, DateOnly Ausstellungsdatum, DateTimeOffset EingesehenAm, Guid PruefendePersonId)
    : ICommand, IModulCommand<OrganisationenDbContext>, IEreignisEmpfaenger<NachweisBestaetigungSpeichern, NachweisEingesehen>
{
    public static NachweisBestaetigungSpeichern Aus(Umschlag umschlag, NachweisEingesehen ereignis) =>
        new(umschlag, ereignis.PersonId, ereignis.Art, ereignis.Ausstellungsdatum, ereignis.EingesehenAm, ereignis.PruefendePersonId);
}

internal sealed class NachweisBestaetigungSpeichernHandler(OrganisationenDbContext db) : ICommandHandler<NachweisBestaetigungSpeichern>
{
    public async ValueTask<Unit> Handle(NachweisBestaetigungSpeichern command, CancellationToken cancellationToken)
    {
        var organisationId = command.Umschlag.OrganisationId
                             ?? throw new InvalidOperationException("Eine Bestätigung gehört immer zu einer Organisation (Umschlag ohne Mandant).");
        var monate = await db.Richtlinien
                         .Where(r => r.OrganisationId == organisationId && r.Art == command.Art)
                         .Select(r => (int?)r.GueltigkeitMonate)
                         .SingleOrDefaultAsync(cancellationToken)
                     ?? NachweisRichtlinie.VorgabeGueltigkeit(command.Art);
        var gueltigBis = command.Ausstellungsdatum.AddMonths(monate);

        var vorhanden = await db.Bestaetigungen.SingleOrDefaultAsync(
            b => b.OrganisationId == organisationId && b.PersonId == command.PersonId && b.Art == command.Art, cancellationToken);
        if (vorhanden is null)
        {
            db.Bestaetigungen.Add(NachweisBestaetigung.Anlegen(organisationId, command.PersonId, command.Art, command.EingesehenAm, gueltigBis, command.PruefendePersonId));
        }
        else
        {
            vorhanden.Uebernehmen(command.EingesehenAm, gueltigBis, command.PruefendePersonId);
        }

        return Unit.Value;
    }
}
