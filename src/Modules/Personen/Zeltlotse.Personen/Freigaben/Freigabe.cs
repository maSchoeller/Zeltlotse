using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Freizeiten.Contracts.Events;
using Zeltlotse.Personen.Contracts.Freizeitpass;
using Zeltlotse.Personen.Daten;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Personen.Freigaben;

/// <summary>Stufe einer <see cref="Freigabe"/> (AD-8); gespeichert als Zahl, nur anhängen.</summary>
internal enum FreigabeStufe : short
{
    Leitung = 1,
    Team = 2,
}

/// <summary>
/// Freigabe einer Person für eine Freizeit (AD-8): entsteht mit dem Eingang der Anmeldung in der Stufe Leitung, wird mit der
/// Bestätigung zur Stufe Team und gilt bis zum Ende des Freigabefensters; eine Absage beendet sie sofort (die Zeile bleibt für
/// Auskunft und Idempotenz). Organisationseigen (Mandantenfilter), gehört aber der Person (personeneigen, AD-16). Je Anmeldung
/// höchstens eine. Story 2-5: <see cref="Fragen"/> hält die Kennungen der abgefragten Fragen; nur sie sieht die Freizeit.
/// </summary>
internal sealed class Freigabe : IOrganisationsEigen
{
    private Freigabe()
    {
    }

    public Guid Id { get; private init; }

    public Guid OrganisationId { get; private init; }

    public Guid PersonId { get; private init; }

    public Guid FreizeitId { get; private init; }

    public Guid AnmeldungId { get; private init; }

    public FreigabeStufe Stufe { get; private set; }

    public DateTimeOffset GueltigBis { get; private set; }

    public DateTimeOffset AngelegtAm { get; private init; }

    /// <summary>
    /// Story 2-5: die Katalog-Kennungen der Fragen, die die Freizeit beim Eingang abgefragt hat, mit der Bestätigung ersetzt; danach fest.
    /// Freigaben von vor Story 2-5 haben keine.
    /// </summary>
    public string[] Fragen { get; private set; } = [];

    public static Freigabe Leitung(
        Guid id, Guid organisationId, Guid personId, Guid freizeitId, Guid anmeldungId, DateTimeOffset gueltigBis, DateTimeOffset jetzt,
        IReadOnlyList<string> fragen) => new()
    {
        Fragen = [.. fragen],
        Id = id,
        OrganisationId = organisationId,
        PersonId = personId,
        FreizeitId = freizeitId,
        AnmeldungId = anmeldungId,
        Stufe = FreigabeStufe.Leitung,
        GueltigBis = gueltigBis,
        AngelegtAm = jetzt,
    };

    /// <summary>
    /// Mit der Bestätigung sieht auch das Team den Freizeitpass (AD-8); „gültig bis“ bleibt. Die Fragen werden durch die der Bestätigung
    /// ersetzt (Story 2-5); ohne Angabe (Ereignis von vor Story 2-5) bleiben die bisherigen.
    /// </summary>
    public void ZuTeam(IReadOnlyList<string>? fragen)
    {
        Stufe = FreigabeStufe.Team;
        if (fragen is not null)
        {
            Fragen = [.. fragen];
        }
    }

    /// <summary>Eine Absage beendet die Freigabe sofort: „gültig bis“ wird <paramref name="jetzt"/>, ein früheres Ende bleibt.</summary>
    public void Beenden(DateTimeOffset jetzt)
    {
        if (jetzt < GueltigBis)
        {
            GueltigBis = jetzt;
        }
    }
}

/// <summary>
/// Empfänger von <see cref="AnmeldungEingegangen"/> (Tabelle Ereignis-Fluss, AD-4, AD-8): legt die Freigabe der Stufe Leitung an,
/// gültig bis zum Ende des Freigabefensters der Freizeit. Idempotent je Anmeldung; ist die Person inzwischen gelöscht, entsteht
/// nichts. Der Mandant kommt aus dem Umschlag (AD-6); kein Benutzer-Command.
/// </summary>
internal sealed record FreigabeAnlegen(
    Umschlag Umschlag, Guid AnmeldungId, Guid FreizeitId, Guid PersonId, DateTimeOffset GueltigBis, IReadOnlyList<FreizeitpassFrage>? Fragen = null)
    : ICommand, IModulCommand<PersonenDbContext>, IEreignisEmpfaenger<FreigabeAnlegen, AnmeldungEingegangen>
{
    public static FreigabeAnlegen Aus(Umschlag umschlag, AnmeldungEingegangen ereignis) =>
        new(umschlag, ereignis.AnmeldungId, ereignis.FreizeitId, ereignis.PersonId, ereignis.FreigabeGueltigBis, ereignis.Fragen);
}

internal sealed class FreigabeAnlegenHandler(PersonenDbContext db, TimeProvider uhr) : ICommandHandler<FreigabeAnlegen>
{
    public async ValueTask<Unit> Handle(FreigabeAnlegen command, CancellationToken cancellationToken)
    {
        var organisationId = command.Umschlag.OrganisationId
                             ?? throw new InvalidOperationException("Eine Anmeldung gehört immer zu einer Organisation (Umschlag ohne Mandant).");
        if (await db.Freigaben.AnyAsync(f => f.AnmeldungId == command.AnmeldungId, cancellationToken)
            || !await db.Personen.AnyAsync(p => p.Id == command.PersonId, cancellationToken))
        {
            return Unit.Value;
        }

        db.Freigaben.Add(Freigabe.Leitung(
            Kennung.Neu(uhr), organisationId, command.PersonId, command.FreizeitId, command.AnmeldungId, command.GueltigBis, uhr.GetUtcNow(),
            FreigabeFragen.Kennungen(command.Fragen) ?? []));
        return Unit.Value;
    }
}

/// <summary>
/// Empfänger von <see cref="AnmeldungBestaetigt"/> (Tabelle Ereignis-Fluss, AD-8): setzt die Freigabe der Anmeldung auf die Stufe
/// Team. Ohne Freigabe-Zeile (Person gelöscht) tut er nichts; eine wiederholte Zustellung verwirft die Inbox. Mandant aus dem
/// Umschlag; kein Benutzer-Command.
/// </summary>
internal sealed record FreigabeTeamSetzen(Umschlag Umschlag, Guid AnmeldungId, IReadOnlyList<FreizeitpassFrage>? Fragen = null)
    : ICommand, IModulCommand<PersonenDbContext>, IEreignisEmpfaenger<FreigabeTeamSetzen, AnmeldungBestaetigt>
{
    public static FreigabeTeamSetzen Aus(Umschlag umschlag, AnmeldungBestaetigt ereignis) => new(umschlag, ereignis.AnmeldungId, ereignis.Fragen);
}

internal sealed class FreigabeTeamSetzenHandler(PersonenDbContext db) : ICommandHandler<FreigabeTeamSetzen>
{
    public async ValueTask<Unit> Handle(FreigabeTeamSetzen command, CancellationToken cancellationToken)
    {
        var freigabe = await db.Freigaben.SingleOrDefaultAsync(f => f.AnmeldungId == command.AnmeldungId, cancellationToken);
        freigabe?.ZuTeam(FreigabeFragen.Kennungen(command.Fragen));
        return Unit.Value;
    }
}

/// <summary>
/// Empfänger von <see cref="AnmeldungAbgesagt"/> (Tabelle Ereignis-Fluss, AD-8): beendet die Freigabe der Anmeldung zum Zeitpunkt
/// der Zustellung; die Zeile bleibt. Ohne Freigabe-Zeile tut er nichts. Mandant aus dem Umschlag; kein Benutzer-Command.
/// </summary>
internal sealed record FreigabeBeenden(Umschlag Umschlag, Guid AnmeldungId)
    : ICommand, IModulCommand<PersonenDbContext>, IEreignisEmpfaenger<FreigabeBeenden, AnmeldungAbgesagt>
{
    public static FreigabeBeenden Aus(Umschlag umschlag, AnmeldungAbgesagt ereignis) => new(umschlag, ereignis.AnmeldungId);
}

internal sealed class FreigabeBeendenHandler(PersonenDbContext db, TimeProvider uhr) : ICommandHandler<FreigabeBeenden>
{
    public async ValueTask<Unit> Handle(FreigabeBeenden command, CancellationToken cancellationToken)
    {
        var freigabe = await db.Freigaben.SingleOrDefaultAsync(f => f.AnmeldungId == command.AnmeldungId, cancellationToken);
        freigabe?.Beenden(uhr.GetUtcNow());
        return Unit.Value;
    }
}

/// <summary>
/// Story 2-5, AD-4: Ereignisse tragen die Fragen als <see cref="FreizeitpassFrage"/>; die Freigabe speichert die Katalog-Kennungen
/// (<see cref="Fragenkatalog.KennungAusPascalName"/>), in Katalogreihenfolge ohne Doppelte.
/// </summary>
internal static class FreigabeFragen
{
    /// <summary>Die Kennungen der Fragen; <c>null</c> bleibt <c>null</c> (Ereignis von vor Story 2-5).</summary>
    public static IReadOnlyList<string>? Kennungen(IEnumerable<FreizeitpassFrage>? fragen) =>
        fragen is null
            ? null
            : Fragenkatalog.Ordnen(fragen.Select(f => Fragenkatalog.KennungAusPascalName(f.ToString())
                                                      ?? throw new InvalidOperationException($"{nameof(FreizeitpassFrage)}.{f} hat keine Frage im Katalog.")));
}
