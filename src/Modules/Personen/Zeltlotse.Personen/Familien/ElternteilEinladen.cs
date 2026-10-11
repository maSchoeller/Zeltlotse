using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Personen.Daten;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Zugangslinks;

namespace Zeltlotse.Personen.Familien;

/// <summary>
/// Lädt einen zweiten Elternteil ins Familienkonto ein (FR-3, AD-11). Fehlt die Familie, entsteht sie mit der Person als
/// erstem Elternteil. Hat die Familie schon zwei Eltern, ist das Ergebnis <see cref="ElternteilEinladung.Voll"/>. Eine neue
/// Einladung verwirft ältere offene Einladungen derselben Familie.
/// </summary>
internal sealed record ElternteilEinladenCommand(Guid ElternPersonId, string? Mail)
    : ICommand<ElternteilEinladung>, IModulCommand<PersonenDbContext>, IBenutzerCommand
{
    /// <summary>Nie die Mail-Adresse ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(ElternteilEinladenCommand)}({ElternPersonId})";
}

/// <summary>Ergebnis einer Einladung.</summary>
internal abstract record ElternteilEinladung
{
    private ElternteilEinladung()
    {
    }

    /// <summary>Die eben angelegte Einladung; das Token im Klartext gibt es nur hier, für die Mail (AD-11).</summary>
    public sealed record Neu(Guid EinladungId, string Mail, string Token) : ElternteilEinladung
    {
        /// <summary>Nie Mail-Adresse oder Token ausgeben (AD-22).</summary>
        public override string ToString() => $"Neu({EinladungId})";
    }

    /// <summary>Die Familie hat schon zwei Eltern: <c>409</c>.</summary>
    public sealed record Voll : ElternteilEinladung;
}

internal sealed class ElternteilEinladenValidator : AbstractValidator<ElternteilEinladenCommand>
{
    public ElternteilEinladenValidator() => RuleFor(c => c.Mail).MailAdresse();
}

internal sealed class ElternteilEinladenHandler(PersonenDbContext db, TimeProvider uhr) : ICommandHandler<ElternteilEinladenCommand, ElternteilEinladung>
{
    public async ValueTask<ElternteilEinladung> Handle(ElternteilEinladenCommand command, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({command.ElternPersonId.ToString()}, 0))", cancellationToken);
        var (familieId, neu) = await FamilienSperre.SicherstellenAsync(db.Familien, db.Elternteile, command.ElternPersonId, uhr, cancellationToken);
        if (!neu)
        {
            // Zeilensperre auf die Familie: Einladen und Annehmen derselben Familie laufen nacheinander.
            await FamilienSperre.FamilieAsync(db.Familien, familieId, cancellationToken);
            if (await db.Elternteile.CountAsync(e => e.FamilieId == familieId, cancellationToken) >= Familie.HoechstensEltern)
            {
                return new ElternteilEinladung.Voll();
            }
        }

        var offene = await db.FamilienEinladungen
            .Where(e => e.FamilieId == familieId && e.Status == FamilienEinladungStatus.Offen)
            .ToListAsync(cancellationToken);
        foreach (var aeltere in offene)
        {
            aeltere.Verwerfen();
        }

        var normalisiert = MailAdresse.Normalisieren(command.Mail!);
        var token = Zugangslink.NeuesToken();
        var einladung = FamilienEinladung.Anlegen(
            Kennung.Neu(uhr), familieId, normalisiert, Zugangslink.TokenHash(FamilienEinladung.Zweck, token), command.ElternPersonId, uhr.GetUtcNow());
        db.FamilienEinladungen.Add(einladung);
        return new ElternteilEinladung.Neu(einladung.Id, normalisiert, token);
    }
}
