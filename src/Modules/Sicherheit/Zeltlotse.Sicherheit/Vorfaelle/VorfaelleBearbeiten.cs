using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Gemeinsam;
using Zeltlotse.Api.Contracts.Sicherheit;
using Zeltlotse.Freizeiten.Contracts.Events;
using Zeltlotse.Freizeiten.Contracts.Namen;
using Zeltlotse.Freizeiten.Contracts.Personen;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Personen.Contracts.Namen;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.Sicherheit.Daten;
using ApiVorfallEintrag = Zeltlotse.Api.Contracts.Sicherheit.VorfallEintrag;

namespace Zeltlotse.Sicherheit.Vorfaelle;

/// <summary>
/// Story 2-10: Legt einen Eintrag im Vorfall-Protokoll an (Leitung und Team, FR-31). Der Text wird getrimmt und nur verschlüsselt mit dem
/// Schlüssel der betroffenen Person gespeichert (AD-14). Gibt es die Kennung in dieser Freizeit schon, bleibt es bei der einen Zeile (AD-18);
/// in einer anderen Freizeit ist das ein Konflikt. Eine abgeschlossene Freizeit ist nur lesbar (AD-17). Story 3-4: Eine neue Entnahme geht als
/// <c>ErsteHilfeEntnommen</c> an Inventar (Schadens- und Verlustliste).
/// </summary>
internal sealed record VorfallAnlegenCommand(Guid FreizeitId, Guid Id, Guid PersonId, Guid VerfasstVon, VorfallArt Art, string? Text)
    : ICommand<VorfallAnlegen>, IModulCommand<SicherheitDbContext>, IBenutzerCommand
{
    /// <summary>Keinen Text und keine Personenkennung ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(VorfallAnlegenCommand)}({FreizeitId}, {Id}, {Art})";
}

/// <summary>Ergebnis von <see cref="VorfallAnlegenCommand"/>.</summary>
internal enum VorfallAnlegen
{
    /// <summary>Angelegt oder mit dieser Kennung in dieser Freizeit schon vorhanden.</summary>
    Angelegt,

    /// <summary>Die Person gehört nicht zur Freizeit, oder ihr Schlüssel fehlt.</summary>
    UnbekanntePerson,

    /// <summary>Die Kennung gibt es schon in einer anderen Freizeit.</summary>
    Konflikt,

    Abgeschlossen,

    NichtGefunden,
}

internal sealed class VorfallAnlegenValidator : AbstractValidator<VorfallAnlegenCommand>
{
    public VorfallAnlegenValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (c.Id == Guid.Empty)
            {
                kontext.AddFailure(new ValidationFailure(VorfallFelder.Id, "Die Kennung fehlt."));
            }

            if (!Enum.IsDefined(c.Art))
            {
                kontext.AddFailure(new ValidationFailure(VorfallFelder.Art, "Unbekannte Art."));
            }

            var text = c.Text?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                kontext.AddFailure(new ValidationFailure(VorfallFelder.Text, "Text fehlt."));
            }
            else if (text.Length > VorfallFelder.TextMaximaleLaenge)
            {
                kontext.AddFailure(new ValidationFailure(VorfallFelder.Text, $"Höchstens {VorfallFelder.TextMaximaleLaenge} Zeichen."));
            }
        });
}

internal sealed class VorfallAnlegenHandler(
    SicherheitDbContext db, IMandantenKontext mandant, IFreizeitZeitpunkte zeitpunkte, IFreizeitPersonenLesen personen,
    IVerschluesselung verschluesselung, TimeProvider uhr)
    : ICommandHandler<VorfallAnlegenCommand, VorfallAnlegen>
{
    public async ValueTask<VorfallAnlegen> Handle(VorfallAnlegenCommand command, CancellationToken cancellationToken)
    {
        if (mandant.OrganisationId is not { } organisationId
            || !(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return VorfallAnlegen.NichtGefunden;
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return VorfallAnlegen.Abgeschlossen;
        }

        // Doppelt gesendet (AD-18): Dieselbe Kennung in dieser Freizeit ist schon gespeichert.
        if (await VorhandenAsync(command.Id, cancellationToken) is { } vorhanden)
        {
            return vorhanden == command.FreizeitId ? VorfallAnlegen.Angelegt : VorfallAnlegen.Konflikt;
        }

        if (!await personen.GehoertDazuAsync(command.FreizeitId, command.PersonId, cancellationToken))
        {
            return VorfallAnlegen.UnbekanntePerson;
        }

        Geschuetzt text;
        try
        {
            text = await verschluesselung.VerschluesselnAsync(command.PersonId, command.Text!.Trim(), cancellationToken);
        }
        catch (PersonenSchluesselFehltAusnahme)
        {
            return VorfallAnlegen.UnbekanntePerson;
        }

        var eintrag = VorfallEintrag.Anlegen(
            command.Id, organisationId, command.FreizeitId, command.PersonId, command.VerfasstVon, command.Art, text, uhr.GetUtcNow());
        // Story 3-4: Eine Entnahme meldet ErsteHilfeEntnommen; die Folgenummer danach steht im INSERT.
        eintrag.ErsteHilfeMelden();
        // Eindeutig je Kennung: Auch zwei gleichzeitige Aufrufe mit derselben Kennung ergeben genau eine Zeile.
        var angelegt = await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO sicherheit.vorfall_eintrag ("Id", "OrganisationId", "FreizeitId", "PersonId", "VerfasstVon", "Art", "Text", "ErfasstAm", "Folgenummer")
             VALUES ({eintrag.Id}, {eintrag.OrganisationId}, {eintrag.FreizeitId}, {eintrag.PersonId}, {eintrag.VerfasstVon}, {(short)eintrag.Art},
                     {eintrag.Text.AlsBytes()}, {eintrag.ErfasstAm}, {eintrag.Folgenummer})
             ON CONFLICT ("Id") DO NOTHING
             """,
            cancellationToken);
        if (angelegt == 1)
        {
            // Nur ein frisch angelegter Eintrag kommt unverändert an den DbContext: Die Outbox-Stufe sammelt sein Ereignis genau einmal ein. Doppelt
            // gesendete Einträge (AD-18) bleiben ohne zweites Ereignis.
            db.VorfallEintraege.Attach(eintrag);
            return VorfallAnlegen.Angelegt;
        }

        return await VorhandenAsync(command.Id, cancellationToken) == command.FreizeitId ? VorfallAnlegen.Angelegt : VorfallAnlegen.Konflikt;
    }

    /// <summary>Die Freizeit eines Eintrags mit dieser Kennung, auch in einer anderen Organisation (die Kennung ist global eindeutig).</summary>
    private async Task<Guid?> VorhandenAsync(Guid id, CancellationToken abbruch) =>
        (await db.Database.SqlQuery<Guid>($"SELECT \"FreizeitId\" AS \"Value\" FROM sicherheit.vorfall_eintrag WHERE \"Id\" = {id}").ToListAsync(abbruch))
        .Cast<Guid?>()
        .SingleOrDefault();
}

/// <summary>
/// Story 2-10: Empfänger von <see cref="FreizeitAbgeschlossen"/> (Tabelle Ereignis-Fluss, AD-16): legt alle Einträge des Vorfall-Protokolls
/// der Freizeit ins Archiv der Organisation. Schon archivierte behalten ihren Zeitpunkt; eine wiederholte Zustellung ändert nichts. Mandant
/// aus dem Umschlag; kein Benutzer-Command.
/// </summary>
internal sealed record VorfaelleArchivieren(Umschlag Umschlag, Guid FreizeitId)
    : ICommand, IModulCommand<SicherheitDbContext>, IEreignisEmpfaenger<VorfaelleArchivieren, FreizeitAbgeschlossen>
{
    public static VorfaelleArchivieren Aus(Umschlag umschlag, FreizeitAbgeschlossen ereignis) => new(umschlag, ereignis.FreizeitId);
}

internal sealed class VorfaelleArchivierenHandler(SicherheitDbContext db, TimeProvider uhr) : ICommandHandler<VorfaelleArchivieren>
{
    public async ValueTask<Unit> Handle(VorfaelleArchivieren command, CancellationToken cancellationToken)
    {
        var jetzt = uhr.GetUtcNow();
        foreach (var eintrag in await db.VorfallEintraege.Where(e => e.FreizeitId == command.FreizeitId && e.ArchiviertAm == null).ToListAsync(cancellationToken))
        {
            eintrag.Archivieren(jetzt);
        }

        return Unit.Value;
    }
}

/// <summary>Gemeinsame Lesart der Einträge für Protokoll, Startseite und Archiv: Namen aus Personen, Text entschlüsselt.</summary>
internal static class VorfallLesen
{
    /// <summary>Wie gelöschte Personen heißen (wie bei Anmeldungen und Team-Notizen).</summary>
    public const string GeloeschtePersonText = "Gelöschte Person";

    public static async Task<List<ApiVorfallEintrag>> AlsApiAsync(
        IReadOnlyList<VorfallEintrag> eintraege, IPersonenNamen namen, IVerschluesselung verschluesselung, CancellationToken abbruch)
    {
        var personen = eintraege.SelectMany(e => new[] { e.PersonId, e.VerfasstVon }).Where(p => p != GeloeschtePerson.Id).Distinct().ToList();
        var bekannt = personen.Count == 0 ? new Dictionary<Guid, PersonenName>() : await namen.NamenAsync(personen, abbruch);
        var ergebnis = new List<ApiVorfallEintrag>(eintraege.Count);
        foreach (var eintrag in eintraege)
        {
            var (vorname, nachname) = Name(eintrag.PersonId, bekannt);
            var (verfasserVorname, verfasserNachname) = Name(eintrag.VerfasstVon, bekannt);
            ergebnis.Add(new ApiVorfallEintrag(
                eintrag.Id, eintrag.PersonId, vorname, nachname, eintrag.Art, await TextAsync(verschluesselung, eintrag.Text, abbruch), eintrag.ErfasstAm,
                verfasserVorname, verfasserNachname));
        }

        return ergebnis;
    }

    /// <summary>Der Klartext; ist der Schlüssel der betroffenen Person vernichtet, <c>null</c>.</summary>
    public static async Task<string?> TextAsync(IVerschluesselung verschluesselung, Geschuetzt text, CancellationToken abbruch)
    {
        try
        {
            return await verschluesselung.EntschluesselnAsync(text, abbruch);
        }
        catch (PersonenSchluesselFehltAusnahme)
        {
            return null;
        }
    }

    private static (string? Vorname, string? Nachname) Name(Guid personId, IReadOnlyDictionary<Guid, PersonenName> bekannt) =>
        personId == GeloeschtePerson.Id
            ? (GeloeschtePersonText, null)
            : bekannt.GetValueOrDefault(personId) is { } name ? (name.Vorname, name.Nachname) : (null, null);
}

/// <summary>
/// <c>GET /api/freizeiten/{freizeitId}/sicherheit/vorfaelle[?personId=]</c> (Leitung und Team, FR-31): die Einträge der Freizeit, mit
/// <c>personId</c> nur die dieser Person (für den Platzhalter gelöschter Personen keine); neueste zuerst. Die Startseite der Leitung zeigt sie als Vorfall-Notizen.
/// </summary>
internal static class VorfaelleLadenEndpunkt
{
    public static async Task<Ok<List<ApiVorfallEintrag>>> Laden(
        Guid freizeitId, Guid? personId, SicherheitLeseDbContext db, IPersonenNamen namen, IVerschluesselung verschluesselung, CancellationToken abbruch)
    {
        // Der Platzhalter steht für alle gelöschten Personen; ihre Einträge sind kein gemeinsames Protokoll.
        if (personId == GeloeschtePerson.Id)
        {
            return TypedResults.Ok(new List<ApiVorfallEintrag>());
        }

        var abfrage = db.VorfallEintraege.Where(e => e.FreizeitId == freizeitId);
        if (personId is { } person)
        {
            abfrage = abfrage.Where(e => e.PersonId == person);
        }

        var eintraege = await abfrage.OrderByDescending(e => e.ErfasstAm).ThenByDescending(e => e.Id).ToListAsync(abbruch);
        return TypedResults.Ok(await VorfallLesen.AlsApiAsync(eintraege, namen, verschluesselung, abbruch));
    }
}

/// <summary>
/// <c>POST /api/freizeiten/{freizeitId}/sicherheit/vorfaelle</c> (Leitung und Team, FR-31): Körper <see cref="VorfallEintragNeu"/>; die
/// verfassende Person ist die angemeldete. <c>201</c> mit <c>Location</c>, auch wenn die Kennung in dieser Freizeit schon gespeichert ist;
/// Formfehler und eine Person, die nicht zur Freizeit gehört, <c>400</c> am Feld; Kennung einer anderen Freizeit <c>409 konflikt</c>;
/// abgeschlossene Freizeit <c>409 freizeit-abgeschlossen</c>.
/// </summary>
internal static class VorfallAnlegenEndpunkt
{
    public const string UnbekanntePerson = "Diese Person gehört nicht zur Freizeit.";

    public static async Task<IResult> Anlegen(Guid freizeitId, VorfallEintragNeu koerper, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch)
    {
        var ich = angemeldet.PersonId ?? throw new InvalidOperationException("Ohne Sitzung erreicht niemand diesen Endpunkt.");
        return await sender.Send(new VorfallAnlegenCommand(freizeitId, koerper.Id, koerper.PersonId, ich, koerper.Art, koerper.Text), abbruch) switch
        {
            VorfallAnlegen.Angelegt => TypedResults.Created($"/api/freizeiten/{freizeitId}/sicherheit/vorfaelle?personId={koerper.PersonId}"),
            VorfallAnlegen.UnbekanntePerson => throw new ValidierungsAusnahme(new Dictionary<string, string[]> { [VorfallFelder.PersonId] = [UnbekanntePerson] }),
            VorfallAnlegen.Konflikt => VorfallProbleme.Problem(Problemcodes.Konflikt, "Diese Kennung gehört zu einem anderen Eintrag."),
            VorfallAnlegen.Abgeschlossen => VorfallProbleme.Problem(Problemcodes.FreizeitAbgeschlossen, "Diese Freizeit ist abgeschlossen und nur lesbar."),
            _ => TypedResults.NotFound(),
        };
    }
}

/// <summary>
/// <c>GET /api/organisationen/{orgId}/sicherheit/vorfall-archiv</c> (Org-Admin, FR-31, AD-16): die archivierten Einträge der
/// abgeschlossenen Freizeiten der Organisation, je Freizeit gruppiert; Freizeiten nach ihrem neuesten Eintrag, Einträge neueste zuerst.
/// </summary>
internal static class VorfallArchivEndpunkt
{
    public static async Task<Ok<List<VorfallArchivFreizeit>>> Laden(
        SicherheitLeseDbContext db, IFreizeitNamenLesen freizeitNamen, IPersonenNamen namen, IVerschluesselung verschluesselung,
        CancellationToken abbruch)
    {
        var eintraege = await db.VorfallEintraege
            .Where(e => e.ArchiviertAm != null)
            .OrderByDescending(e => e.ErfasstAm)
            .ThenByDescending(e => e.Id)
            .ToListAsync(abbruch);
        var gruppen = eintraege.GroupBy(e => e.FreizeitId).ToList();
        var freizeiten = await freizeitNamen.NamenAsync([.. gruppen.Select(g => g.Key)], abbruch);
        var ergebnis = new List<VorfallArchivFreizeit>(gruppen.Count);
        foreach (var gruppe in gruppen)
        {
            ergebnis.Add(new VorfallArchivFreizeit(
                gruppe.Key, freizeiten.GetValueOrDefault(gruppe.Key) ?? "", await VorfallLesen.AlsApiAsync([.. gruppe], namen, verschluesselung, abbruch)));
        }

        return TypedResults.Ok(ergebnis);
    }
}

internal static class VorfallProbleme
{
    public static ProblemHttpResult Problem(string code, string titel) => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: titel,
        extensions: new Dictionary<string, object?> { [Problemcodes.Feld] = code });
}
