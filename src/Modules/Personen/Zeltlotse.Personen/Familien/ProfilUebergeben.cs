using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Personen.Daten;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Zugangslinks;

namespace Zeltlotse.Personen.Familien;

/// <summary>
/// Löst die Übergabe eines Kinderprofils an das volljährige Kind aus (FR-4, NS-11): nur durch einen Elternteil der Familie
/// des Kindes (sonst <see cref="UebergabeAusloesung.NichtGefunden"/>) und nur bei volljährigem Kind (sonst
/// <see cref="UebergabeAusloesung.NichtVolljaehrig"/>). Eine neue Übergabe verwirft ältere offene desselben Kindes. Die
/// erneute Anmeldung (AD-25) prüft der Host am Endpunkt.
/// </summary>
internal sealed record ProfilUebergebenCommand(Guid ElternPersonId, Guid KindId, string? Mail)
    : ICommand<UebergabeAusloesung>, IModulCommand<PersonenDbContext>, IBenutzerCommand
{
    /// <summary>Nie die Mail-Adresse ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(ProfilUebergebenCommand)}({KindId})";
}

/// <summary>Ergebnis des Auslösens einer Übergabe.</summary>
internal abstract record UebergabeAusloesung
{
    private UebergabeAusloesung()
    {
    }

    /// <summary>Die eben ausgelöste Übergabe; Token und Vorname im Klartext nur für die Mail (AD-11).</summary>
    public sealed record Neu(Guid UebergabeId, Guid KindId, string? Vorname, string Mail, string Token) : UebergabeAusloesung
    {
        /// <summary>Nie Name, Mail-Adresse oder Token ausgeben (AD-22).</summary>
        public override string ToString() => $"Neu({UebergabeId})";
    }

    /// <summary>Kein Kind der eigenen Familie: <c>404</c>.</summary>
    public sealed record NichtGefunden : UebergabeAusloesung;

    /// <summary>Das Kind ist noch nicht volljährig: <c>409</c>.</summary>
    public sealed record NichtVolljaehrig : UebergabeAusloesung;
}

internal sealed class ProfilUebergebenValidator : AbstractValidator<ProfilUebergebenCommand>
{
    public ProfilUebergebenValidator() => RuleFor(c => c.Mail).MailAdresse();
}

internal sealed class ProfilUebergebenHandler(PersonenDbContext db, TimeProvider uhr) : ICommandHandler<ProfilUebergebenCommand, UebergabeAusloesung>
{
    public async ValueTask<UebergabeAusloesung> Handle(ProfilUebergebenCommand command, CancellationToken cancellationToken)
    {
        var elternteil = await db.Elternteile.SingleOrDefaultAsync(e => e.PersonId == command.ElternPersonId, cancellationToken);
        var kind = elternteil is null ? null : await FamilienSperre.KindAsync(db.Personen, command.KindId, elternteil.FamilieId, cancellationToken);
        if (kind is null)
        {
            return new UebergabeAusloesung.NichtGefunden();
        }

        if (kind.Geburtsdatum is not { } geburtsdatum || !Volljaehrigkeit.Ist(geburtsdatum, Volljaehrigkeit.Heute(uhr)))
        {
            return new UebergabeAusloesung.NichtVolljaehrig();
        }

        var offene = await db.Uebergaben
            .Where(u => u.PersonId == kind.Id && u.Status == UebergabeStatus.Offen)
            .ToListAsync(cancellationToken);
        foreach (var aeltere in offene)
        {
            aeltere.Verwerfen();
        }

        var normalisiert = MailAdresse.Normalisieren(command.Mail!);
        var token = Zugangslink.NeuesToken();
        var uebergabe = ProfilUebergabe.Ausloesen(
            Kennung.Neu(uhr), elternteil!.FamilieId, kind.Id, normalisiert, Zugangslink.TokenHash(ProfilUebergabe.Zweck, token), command.ElternPersonId, uhr.GetUtcNow());
        db.Uebergaben.Add(uebergabe);
        return new UebergabeAusloesung.Neu(uebergabe.Id, kind.Id, kind.Vorname, normalisiert, token);
    }
}
