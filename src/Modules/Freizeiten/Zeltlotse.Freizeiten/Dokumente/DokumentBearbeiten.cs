using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Freizeiten.Daten;
using Zeltlotse.Freizeiten.Domaene;
using Zeltlotse.Organisationen.Contracts.Dokumente;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Dateien;

namespace Zeltlotse.Freizeiten.Dokumente;

/// <summary>Ergebnis der Commands zu Dokumenten der Freizeit.</summary>
internal abstract record DokumentAenderung
{
    private DokumentAenderung()
    {
    }

    /// <summary>Hochgeladen oder geändert: die verfolgte Zeile (nach dem Speichern mit neuer Version).</summary>
    public sealed record Gespeichert(Dokument Dokument) : DokumentAenderung;

    /// <summary>Eingebunden: die neue Zeile und die Angaben des Org-Dokuments.</summary>
    public sealed record Eingebunden(Dokument Dokument, OrgDokumentInfo Info) : DokumentAenderung;

    public sealed record Entfernt : DokumentAenderung;

    /// <summary>Freizeit, Dokument oder Org-Dokument gibt es hier nicht: <c>404</c>.</summary>
    public sealed record NichtGefunden : DokumentAenderung;

    /// <summary>Die Freizeit ist abgeschlossen und nur lesbar: <c>409</c> mit Code <c>freizeit-abgeschlossen</c> (AD-17).</summary>
    public sealed record Abgeschlossen : DokumentAenderung;

    /// <summary>Derselbe Wert wurde inzwischen geändert: <c>409</c> mit Code <c>konflikt</c> (AD-19).</summary>
    public sealed record Konflikt : DokumentAenderung;

    /// <summary>Titel einer Einbindung: <c>400</c> am Feld <c>titel</c>.</summary>
    public sealed record NurOrganisation : DokumentAenderung;

    /// <summary>Das Org-Dokument ist in dieser Freizeit schon eingebunden: <c>409</c> mit Code <c>konflikt</c>.</summary>
    public sealed record SchonEingebunden : DokumentAenderung;
}

/// <summary>Gemeinsame Schritte der Handler: die Freizeit unter dem Mandantenfilter und der Schreibschutz nach dem Abschluss (AD-17).</summary>
internal static class DokumentSchritte
{
    /// <summary><c>null</c>, wenn es weitergehen kann; sonst das Ergebnis (nicht gefunden oder abgeschlossen).</summary>
    public static async Task<(Freizeit? Freizeit, DokumentAenderung? Abbruch)> FreizeitAsync(
        IQueryable<Freizeit> freizeiten, Guid freizeitId, TimeProvider uhr, CancellationToken abbruch)
    {
        var freizeit = await freizeiten.AsNoTracking().SingleOrDefaultAsync(f => f.Id == freizeitId, abbruch);
        if (freizeit is null)
        {
            return (null, new DokumentAenderung.NichtGefunden());
        }

        return freizeit.Phase(uhr.GetUtcNow()) == FreizeitPhase.Gelaufen ? (null, new DokumentAenderung.Abgeschlossen()) : (freizeit, null);
    }
}

/// <summary>
/// Lädt eine Datei als Dokument der Freizeit hoch (Leitung und Team, FR-43): die Datei unverschlüsselt in den Dateispeicher unter
/// <c>freizeiten/&lt;Id&gt;</c>, die Zeile mit dem Titel aus dem Dateinamen und der gewählten Sichtbarkeit. Typ und Größe hat der
/// Endpunkt am Inhalt geprüft.
/// </summary>
internal sealed record DokumentHochladenCommand(Guid FreizeitId, string? Dateiname, DokumentSichtbarkeit Sichtbarkeit, byte[] Inhalt, DateiArt Art)
    : ICommand<DokumentAenderung>, IModulCommand<FreizeitenDbContext>, IBenutzerCommand, IFreizeitCommand
{
    /// <summary>Weder Dateiname noch Inhalt ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(DokumentHochladenCommand)}({FreizeitId}, {Sichtbarkeit})";
}

internal sealed class DokumentHochladenHandler(FreizeitenDbContext db, IDateiSpeicher speicher, TimeProvider uhr)
    : ICommandHandler<DokumentHochladenCommand, DokumentAenderung>
{
    public async ValueTask<DokumentAenderung> Handle(DokumentHochladenCommand command, CancellationToken cancellationToken)
    {
        var (freizeit, ergebnis) = await DokumentSchritte.FreizeitAsync(db.Freizeiten, command.FreizeitId, uhr, cancellationToken);
        if (ergebnis is not null)
        {
            return ergebnis;
        }

        var dokument = Dokument.Hochladen(
            Kennung.Neu(uhr), freizeit!.OrganisationId, freizeit.Id, DokumentDateien.TitelAusDateiname(command.Dateiname), command.Sichtbarkeit, command.Art,
            command.Inhalt.Length, uhr.GetUtcNow());
        await speicher.SpeichernAsync(Dokument.DateiPraefix, dokument.Id, command.Inhalt, DateiPruefung.ContentType(command.Art), cancellationToken);
        db.Dokumente.Add(dokument);
        return new DokumentAenderung.Gespeichert(dokument);
    }
}

/// <summary>
/// Ändert genau ein Feld eines Dokuments (AD-19, Leitung und Team): Titel (nicht bei einer Einbindung) oder Sichtbarkeit. Konfliktregel:
/// Weicht der aktuelle Wert desselben Felds vom mitgeschickten alten Wert ab, ist das Ergebnis <see cref="DokumentAenderung.Konflikt"/>.
/// </summary>
internal sealed record DokumentFeldAendernCommand(Guid FreizeitId, Guid DokumentId, DokumentFeld Feld, uint Version, string? AlterWert, string? NeuerWert)
    : ICommand<DokumentAenderung>, IModulCommand<FreizeitenDbContext>, IBenutzerCommand, IFreizeitCommand
{
    /// <summary>Keine Werte ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(DokumentFeldAendernCommand)}({FreizeitId}, {DokumentId}, {Feld})";
}

internal sealed class DokumentFeldAendernValidator : AbstractValidator<DokumentFeldAendernCommand>
{
    public DokumentFeldAendernValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            switch (c.Feld)
            {
                case DokumentFeld.Titel when DokumentDateien.OhneSteuerzeichen(c.NeuerWert).Length == 0:
                    kontext.AddFailure(new ValidationFailure(DokumentFelder.Titel, "Titel fehlt."));
                    break;
                case DokumentFeld.Titel when DokumentDateien.OhneSteuerzeichen(c.NeuerWert).Length > DokumentFelder.TitelMaximaleLaenge:
                    kontext.AddFailure(new ValidationFailure(DokumentFelder.Titel, $"Höchstens {DokumentFelder.TitelMaximaleLaenge} Zeichen."));
                    break;
                case DokumentFeld.Sichtbarkeit when DokumentRegeln.Sichtbarkeit(c.NeuerWert) is null:
                    kontext.AddFailure(new ValidationFailure(DokumentFelder.Sichtbarkeit, DokumentRegeln.SichtbarkeitUnbekannt));
                    break;
                case DokumentFeld.Titel or DokumentFeld.Sichtbarkeit:
                    break;
                default:
                    kontext.AddFailure(new ValidationFailure("feld", "Unbekanntes Feld."));
                    break;
            }
        });
}

internal sealed class DokumentFeldAendernHandler(FreizeitenDbContext db, TimeProvider uhr)
    : ICommandHandler<DokumentFeldAendernCommand, DokumentAenderung>
{
    public async ValueTask<DokumentAenderung> Handle(DokumentFeldAendernCommand command, CancellationToken cancellationToken)
    {
        // Zeilensperre (FOR UPDATE) unter dem Mandantenfilter: Gleichzeitige Änderungen laufen nacheinander, allein die Regel zum alten
        // Wert entscheidet (wie bei der Freizeitseite).
        var dokument = (await db.Dokumente
                .FromSql($"SELECT *, xmin FROM freizeiten.dokument WHERE \"Id\" = {command.DokumentId} FOR UPDATE")
                .ToListAsync(cancellationToken))
            .SingleOrDefault(d => d.FreizeitId == command.FreizeitId);
        if (dokument is null)
        {
            return new DokumentAenderung.NichtGefunden();
        }

        if ((await DokumentSchritte.FreizeitAsync(db.Freizeiten, command.FreizeitId, uhr, cancellationToken)).Abbruch is { } abbruch)
        {
            return abbruch;
        }

        return dokument.FeldAendern(command.Feld, command.AlterWert, command.NeuerWert) switch
        {
            DokumentFeldErgebnis.Gespeichert => new DokumentAenderung.Gespeichert(dokument),
            DokumentFeldErgebnis.NurOrganisation => new DokumentAenderung.NurOrganisation(),
            _ => new DokumentAenderung.Konflikt(),
        };
    }
}

/// <summary>
/// Entfernt ein Dokument aus der Freizeit (Leitung und Team, Inline-Bestätigung in der Oberfläche). Bei einer eigenen Datei wird auch
/// die Datei gelöscht; bei einer Einbindung bleibt das Org-Dokument unberührt.
/// </summary>
internal sealed record DokumentEntfernenCommand(Guid FreizeitId, Guid DokumentId)
    : ICommand<DokumentAenderung>, IModulCommand<FreizeitenDbContext>, IBenutzerCommand, IFreizeitCommand;

internal sealed class DokumentEntfernenHandler(FreizeitenDbContext db, IDateiSpeicher speicher, TimeProvider uhr)
    : ICommandHandler<DokumentEntfernenCommand, DokumentAenderung>
{
    public async ValueTask<DokumentAenderung> Handle(DokumentEntfernenCommand command, CancellationToken cancellationToken)
    {
        var dokument = await db.Dokumente.SingleOrDefaultAsync(d => d.Id == command.DokumentId && d.FreizeitId == command.FreizeitId, cancellationToken);
        if (dokument is null)
        {
            return new DokumentAenderung.NichtGefunden();
        }

        if ((await DokumentSchritte.FreizeitAsync(db.Freizeiten, command.FreizeitId, uhr, cancellationToken)).Abbruch is { } abbruch)
        {
            return abbruch;
        }

        db.Dokumente.Remove(dokument);
        if (!dokument.AusOrganisation)
        {
            await speicher.LoeschenAsync(Dokument.DateiPraefix, dokument.Id, cancellationToken);
        }

        return new DokumentAenderung.Entfernt();
    }
}

/// <summary>
/// Bindet ein Org-Dokument der eigenen Organisation per Kennung ein (Leitung und Team), mit Sichtbarkeit Team. Unbekannt in der
/// Organisation der Freizeit (auch das Dokument einer fremden Organisation) ist <see cref="DokumentAenderung.NichtGefunden"/>; schon
/// eingebunden <see cref="DokumentAenderung.SchonEingebunden"/>. Gleichzeitige Einbindungen desselben Dokuments laufen nacheinander.
/// </summary>
internal sealed record DokumentEinbindenCommand(Guid FreizeitId, Guid? OrgDokumentId)
    : ICommand<DokumentAenderung>, IModulCommand<FreizeitenDbContext>, IBenutzerCommand, IFreizeitCommand;

internal sealed class DokumentEinbindenValidator : AbstractValidator<DokumentEinbindenCommand>
{
    public DokumentEinbindenValidator() =>
        RuleFor(c => c.OrgDokumentId).NotNull().OverridePropertyName(DokumentFelder.OrgDokumentId).WithMessage("Org-Dokument fehlt.");
}

internal sealed class DokumentEinbindenHandler(FreizeitenDbContext db, IOrgDokumenteLesen orgDokumente, TimeProvider uhr)
    : ICommandHandler<DokumentEinbindenCommand, DokumentAenderung>
{
    public async ValueTask<DokumentAenderung> Handle(DokumentEinbindenCommand command, CancellationToken cancellationToken)
    {
        var (freizeit, ergebnis) = await DokumentSchritte.FreizeitAsync(db.Freizeiten, command.FreizeitId, uhr, cancellationToken);
        if (ergebnis is not null)
        {
            return ergebnis;
        }

        var orgDokumentId = command.OrgDokumentId!.Value;
        if (!(await orgDokumente.InfosAsync([orgDokumentId], cancellationToken)).TryGetValue(orgDokumentId, out var info))
        {
            return new DokumentAenderung.NichtGefunden();
        }

        await db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({$"dokument-einbinden:{freizeit!.Id}:{orgDokumentId}"}, 0))", cancellationToken);
        if (await db.Dokumente.AnyAsync(d => d.FreizeitId == freizeit.Id && d.OrgDokumentId == orgDokumentId, cancellationToken))
        {
            return new DokumentAenderung.SchonEingebunden();
        }

        var dokument = Dokument.Einbinden(Kennung.Neu(uhr), freizeit.OrganisationId, freizeit.Id, orgDokumentId, uhr.GetUtcNow());
        db.Dokumente.Add(dokument);
        return new DokumentAenderung.Eingebunden(dokument, info);
    }
}
