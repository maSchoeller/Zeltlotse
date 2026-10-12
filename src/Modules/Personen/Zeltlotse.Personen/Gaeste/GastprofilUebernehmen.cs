using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Personen.Daten;
using Zeltlotse.Personen.Familien;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;

namespace Zeltlotse.Personen.Gaeste;

/// <summary>
/// „Konto aus diesen Daten erstellen“ (FR-17, AD-7, Muster Profil-Übergabe): Das angemeldete Konto übernimmt das Profil des
/// Gast-Elternteils. Gast-Token gültig (nicht abgelaufen, nicht übernommen); sonst <see cref="GastUebernahme.Ungueltig"/> ohne
/// Änderung und <c>LinkEinloesungFehlgeschlagen</c> im Sicherheitsprotokoll. Hat die angemeldete Person schon ein Profil mit Inhalt
/// (Name, Freizeit-Rolle, Elternteil, Freigabe oder Nachweis), ist das Ergebnis <see cref="GastUebernahme.EigenesProfil"/>: Profile
/// werden nie automatisch zusammengeführt.
/// </summary>
/// <remarks>
/// Wirkung in einer Transaktion: Die leere eigene Person wird samt Personen-Schlüssel gelöscht, und der Gast-Zugang legt
/// <c>ProfilUebergabeAngenommen(ElternPersonId, KontoId)</c> ab; danach gilt der Gast-Link nicht mehr. Konten verbindet daraufhin
/// das Konto mit dem Profil des Elternteils (bestehende Zeile der Tabelle Ereignis-Fluss).
/// </remarks>
internal sealed record GastprofilUebernehmenCommand(string? GastToken, Guid KontoId, Guid PersonId)
    : ICommand<GastUebernahme>, IModulCommand<PersonenDbContext>, IBenutzerCommand
{
    /// <summary>Nie das Token ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(GastprofilUebernehmenCommand)}({PersonId})";
}

/// <summary>Ergebnis der Übernahme.</summary>
internal abstract record GastUebernahme
{
    private GastUebernahme()
    {
    }

    public sealed record Uebernommen(Guid ZugangId, Guid ElternPersonId) : GastUebernahme;

    /// <summary>Nicht gültig, abgelaufen oder schon übernommen: <c>404</c>.</summary>
    public sealed record Ungueltig : GastUebernahme;

    /// <summary>Die angemeldete Person hat schon ein eigenes Profil mit Inhalt: <c>409</c>.</summary>
    public sealed record EigenesProfil : GastUebernahme;
}

internal sealed class GastprofilUebernehmenHandler(PersonenDbContext db, ISicherheitsprotokoll protokoll, TimeProvider uhr)
    : ICommandHandler<GastprofilUebernehmenCommand, GastUebernahme>
{
    public async ValueTask<GastUebernahme> Handle(GastprofilUebernehmenCommand command, CancellationToken cancellationToken)
    {
        // Sperre der eigenen Person wie bei „Kind anlegen“ und „Profil übernehmen“; den Gast-Zugang sperrt GastSperre (FOR UPDATE),
        // darauf wartet auch der Löschlauf für Gast-Profile.
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({command.PersonId.ToString()}, 0))", cancellationToken);
        var zugang = await GastSperre.ZugangAsync(db.GastZugaenge, command.GastToken, cancellationToken);
        var jetzt = uhr.GetUtcNow();
        if (zugang is null
            || zugang.ElternPersonId == command.PersonId
            || !zugang.IstGueltig(jetzt, [(await GastFamilie.LesenAsync(db.Elternteile, db.Personen, db.Freigaben, zugang, cancellationToken)).GueltigBis]))
        {
            await protokoll.SchreibenAsync(SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen, command.PersonId, bezugId: zugang?.Id, abbruch: cancellationToken);
            return new GastUebernahme.Ungueltig();
        }

        var eigene = await FamilienSperre.PersonAsync(db.Personen, command.PersonId, cancellationToken);
        if (eigene is { OhneNamen: false }
            // Freizeit-Rollen und Freigaben sind organisationseigen; hier zählt jede Organisation (eigene Person, AD-26).
            || await db.FreizeitRollen.IgnoreQueryFilters().AnyAsync(r => r.PersonId == command.PersonId, cancellationToken)
            || await db.Freigaben.IgnoreQueryFilters().AnyAsync(f => f.PersonId == command.PersonId, cancellationToken)
            || await db.Elternteile.AnyAsync(e => e.PersonId == command.PersonId, cancellationToken)
            || await db.Nachweise.AnyAsync(n => n.PersonId == command.PersonId, cancellationToken))
        {
            return new GastUebernahme.EigenesProfil();
        }

        if (eigene is not null)
        {
            db.Personen.Remove(eigene);
        }

        var schluessel = await db.PersonenSchluessel.SingleOrDefaultAsync(s => s.PersonId == command.PersonId, cancellationToken);
        if (schluessel is not null)
        {
            db.PersonenSchluessel.Remove(schluessel);
        }

        zugang.Uebernehmen(command.KontoId, jetzt);
        return new GastUebernahme.Uebernommen(zugang.Id, zugang.ElternPersonId);
    }
}

/// <summary>
/// <c>POST /api/ich/gastprofil/uebernehmen</c> (angemeldet, zugriffsbegrenzt, AD-26): <c>200 {personId}</c>; ungültig <c>404</c>;
/// eigenes Profil mit Inhalt <c>409</c>. Erfolg steht als <c>ProfilUebergabe</c> im Sicherheitsprotokoll (Person: der Elternteil).
/// Konten verbindet das Konto über das Ereignis; danach meldet sich die Person neu an.
/// </summary>
internal static class GastprofilUebernehmenEndpunkt
{
    public static async Task<Results<Ok<GastprofilUebernommen>, NotFound, ProblemHttpResult>> Uebernehmen(
        GastprofilUebernehmen anfrage, IAngemeldet angemeldet, ISender sender, ISicherheitsprotokoll protokoll, CancellationToken abbruch)
    {
        var ergebnis = await sender.Send(
            new GastprofilUebernehmenCommand(anfrage.GastToken, FamilieAntworten.KontoId(angemeldet), FamilieAntworten.PersonId(angemeldet)), abbruch);
        switch (ergebnis)
        {
            case GastUebernahme.Uebernommen uebernommen:
                await protokoll.SchreibenAsync(SicherheitsEreignisArt.ProfilUebergabe, uebernommen.ElternPersonId, bezugId: uebernommen.ZugangId, abbruch: CancellationToken.None);
                return TypedResults.Ok(new GastprofilUebernommen(uebernommen.ElternPersonId));
            case GastUebernahme.EigenesProfil:
                return FamilieAntworten.Konflikt("Dieses Konto hat schon eigene Angaben.");
            default:
                return TypedResults.NotFound();
        }
    }
}
