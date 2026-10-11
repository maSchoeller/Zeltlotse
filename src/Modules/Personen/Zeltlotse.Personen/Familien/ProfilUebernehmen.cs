using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Personen.Daten;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;
using Zeltlotse.SharedKernel.Zugangslinks;

namespace Zeltlotse.Personen.Familien;

/// <summary>
/// Das volljährige Kind übernimmt sein Profil (FR-4, AD-7): Token gültig, offen, nicht abgelaufen und die Mail-Adresse des
/// angemeldeten Kontos gleich der Übergabe-Mail; sonst <see cref="Uebernahme.Ungueltig"/> ohne Änderung und
/// <c>LinkEinloesungFehlgeschlagen</c> im Sicherheitsprotokoll. Hat die angemeldete Person schon ein eigenes Profil mit Inhalt
/// (Vor- oder Nachname, eine Freizeit-Rolle oder Elternteil einer Familie), ist das Ergebnis
/// <see cref="Uebernahme.EigenesProfil"/>: Profile werden nie automatisch zusammengeführt.
/// </summary>
/// <remarks>
/// Wirkung in einer Transaktion: Das Kind verlässt die Familie (die Eltern verlieren den Zugriff), die leere eigene Person
/// wird samt Personen-Schlüssel gelöscht, und die Übergabe legt <c>ProfilUebergabeAngenommen</c> ab. Konten verbindet daraufhin
/// das Konto mit dem Profil.
/// </remarks>
internal sealed record ProfilUebernehmenCommand(string? Token, string? KontoMail, Guid KontoId, Guid PersonId)
    : ICommand<Uebernahme>, IModulCommand<PersonenDbContext>, IBenutzerCommand
{
    /// <summary>Nie Token oder Mail-Adresse ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(ProfilUebernehmenCommand)}({PersonId})";
}

/// <summary>Ergebnis der Übernahme.</summary>
internal abstract record Uebernahme
{
    private Uebernahme()
    {
    }

    public sealed record Uebernommen(Guid UebergabeId, Guid KindId) : Uebernahme;

    /// <summary>Nicht gültig, abgelaufen, schon benutzt, verworfen oder für eine andere Mail-Adresse: <c>404</c>.</summary>
    public sealed record Ungueltig : Uebernahme;

    /// <summary>Die angemeldete Person hat schon ein eigenes Profil mit Inhalt: <c>409</c>.</summary>
    public sealed record EigenesProfil : Uebernahme;
}

internal sealed class ProfilUebernehmenHandler(PersonenDbContext db, ISicherheitsprotokoll protokoll, TimeProvider uhr)
    : ICommandHandler<ProfilUebernehmenCommand, Uebernahme>
{
    public async ValueTask<Uebernahme> Handle(ProfilUebernehmenCommand command, CancellationToken cancellationToken)
    {
        // Dieselbe Sperre je Person wie „Kind anlegen“ und „Elternteil einladen“: Prüfung und Löschen der eigenen Person
        // laufen nicht gleichzeitig mit einer neuen Familie derselben Person.
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({command.PersonId.ToString()}, 0))", cancellationToken);
        ProfilUebergabe? uebergabe = null;
        if (Zugangslink.KannTokenSein(command.Token))
        {
            uebergabe = await FamilienSperre.UebergabeAsync(db.Uebergaben, Zugangslink.TokenHash(ProfilUebergabe.Zweck, command.Token!), cancellationToken);
        }

        var jetzt = uhr.GetUtcNow();
        var kind = uebergabe is not null && uebergabe.IstAnnehmbar(jetzt) && uebergabe.GiltFuer(command.KontoMail)
            ? await FamilienSperre.KindAsync(db.Personen, uebergabe.PersonId, uebergabe.FamilieId, cancellationToken)
            : null;
        // Ein inzwischen geändertes Geburtsdatum unter 18 macht den Link ungültig: Kein Profil geht an ein minderjähriges Kind.
        if (kind is not null && (kind.Geburtsdatum is not { } geburtsdatum || !Volljaehrigkeit.Ist(geburtsdatum, Volljaehrigkeit.Heute(uhr))))
        {
            kind = null;
        }

        if (kind is null)
        {
            await protokoll.SchreibenAsync(
                SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen, command.PersonId, bezugId: uebergabe?.Id, abbruch: cancellationToken);
            return new Uebernahme.Ungueltig();
        }

        var eigene = await FamilienSperre.PersonAsync(db.Personen, command.PersonId, cancellationToken);
        if (eigene is { OhneNamen: false }
            // Die Liste der Freizeit-Rollen ist organisationseigen; hier zählt jede Organisation (eigene Person, AD-26).
            || await db.FreizeitRollen.IgnoreQueryFilters().AnyAsync(r => r.PersonId == command.PersonId, cancellationToken)
            || await db.Elternteile.AnyAsync(e => e.PersonId == command.PersonId, cancellationToken))
        {
            return new Uebernahme.EigenesProfil();
        }

        kind.FamilieVerlassen();
        if (eigene is not null)
        {
            db.Personen.Remove(eigene);
        }

        var schluessel = await db.PersonenSchluessel.SingleOrDefaultAsync(s => s.PersonId == command.PersonId, cancellationToken);
        if (schluessel is not null)
        {
            db.PersonenSchluessel.Remove(schluessel);
        }

        uebergabe!.Annehmen(command.KontoId, jetzt);
        return new Uebernahme.Uebernommen(uebergabe.Id, kind.Id);
    }
}
