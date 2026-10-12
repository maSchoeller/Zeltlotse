using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Personen;
using Zeltlotse.Personen.Daten;
using Zeltlotse.Personen.Familien;
using Zeltlotse.Personen.Profil;
using Zeltlotse.Personen.Schluessel;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;
using Zeltlotse.SharedKernel.Zugangslinks;

namespace Zeltlotse.Personen.Gaeste;

/// <summary>
/// Feldregeln der Gast-Anmeldung (NFR-12): Pflicht sind Vorname und Mail des Elternteils, Vorname und Geburtsdatum des Kindes. Ein
/// Kind ab 18 meldet sich mit Konto an (FR-18). Jede Regel liefert den Fehlertext oder <c>null</c>.
/// </summary>
internal static class GastRegeln
{
    public static string? Geburtsdatum(DateOnly? geburtsdatum, DateOnly heute) =>
        KindRegeln.Geburtsdatum(geburtsdatum, heute, mitVolljaehrigkeit: false)
        ?? (Volljaehrigkeit.Ist(geburtsdatum!.Value, heute) ? GastFelder.Volljaehrig : null);

    public static void Kind<T>(
        ValidationContext<T> kontext, string vornameFeld, string nachnameFeld, string geburtsdatumFeld, string? vorname, string? nachname, DateOnly? geburtsdatum, DateOnly heute)
    {
        if (KindRegeln.Vorname(vorname) is { } vornameFehler)
        {
            kontext.AddFailure(new ValidationFailure(vornameFeld, vornameFehler));
        }

        if (KindRegeln.Nachname(nachname) is { } nachnameFehler)
        {
            kontext.AddFailure(new ValidationFailure(nachnameFeld, nachnameFehler));
        }

        if (Geburtsdatum(geburtsdatum, heute) is { } datumFehler)
        {
            kontext.AddFailure(new ValidationFailure(geburtsdatumFeld, datumFehler));
        }
    }
}

/// <summary>
/// Legt eine Gast-Anmeldung an (FR-17, AD-7, AD-11): Elternteil ohne Konto mit Namen, Familie, Kind unter 18, je ein
/// Personen-Schlüssel (AD-14) und den Gast-Zugang mit neuem Token, alles in einer Transaktion. Das Token im Klartext gibt es nur im
/// Ergebnis (für die öffentliche Seite und die Eingangsmail).
/// </summary>
internal sealed record GastAnlegenCommand(
    string? ElternVorname, string? ElternNachname, string? Mail, string? KindVorname, string? KindNachname, DateOnly? KindGeburtsdatum)
    : ICommand<GastAnlage>, IModulCommand<PersonenDbContext>, IBenutzerCommand
{
    /// <summary>Keine Namen, Mail-Adressen oder Geburtsdaten ausgeben (AD-22).</summary>
    public override string ToString() => nameof(GastAnlegenCommand);
}

/// <summary>Ergebnis von <see cref="GastAnlegenCommand"/>.</summary>
internal sealed record GastAnlage(string Token, Guid ElternPersonId, Guid KindPersonId)
{
    /// <summary>Nie das Token ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(GastAnlage)}({ElternPersonId}, {KindPersonId})";
}

internal sealed class GastAnlegenValidator : AbstractValidator<GastAnlegenCommand>
{
    public GastAnlegenValidator(TimeProvider uhr)
    {
        RuleFor(c => c).Custom((c, kontext) =>
        {
            GastRegeln.Kind(
                kontext, GastFelder.KindVorname, GastFelder.KindNachname, GastFelder.KindGeburtsdatum, c.KindVorname, c.KindNachname, c.KindGeburtsdatum,
                Volljaehrigkeit.Heute(uhr));
            if (KindRegeln.Vorname(c.ElternVorname) is { } vorname)
            {
                kontext.AddFailure(new ValidationFailure(GastFelder.ElternVorname, vorname));
            }

            if (KindRegeln.Nachname(c.ElternNachname) is { } nachname)
            {
                kontext.AddFailure(new ValidationFailure(GastFelder.ElternNachname, nachname));
            }
        });
        RuleFor(c => c.Mail).MailAdresse();
    }
}

internal sealed class GastAnlegenHandler(PersonenDbContext db, Hauptschluessel hauptschluessel, TimeProvider uhr) : ICommandHandler<GastAnlegenCommand, GastAnlage>
{
    public ValueTask<GastAnlage> Handle(GastAnlegenCommand command, CancellationToken cancellationToken)
    {
        var jetzt = uhr.GetUtcNow();
        var elternId = Kennung.Neu(uhr);
        var familie = Familie.Anlegen(Kennung.Neu(uhr), jetzt);
        var kindId = Kennung.Neu(uhr);
        var token = Zugangslink.NeuesToken();

        db.Personen.Add(Person.Gast(elternId, command.ElternVorname!, command.ElternNachname, jetzt));
        db.PersonenSchluessel.Add(PersonenSchluessel.Anlegen(elternId, hauptschluessel.NeuenPersonenSchluesselUmhuellen(elternId), uhr));
        db.Familien.Add(familie);
        db.Elternteile.Add(FamilienElternteil.Anlegen(familie.Id, elternId));
        db.Personen.Add(Person.Kind(kindId, familie.Id, command.KindVorname!, command.KindNachname, command.KindGeburtsdatum!.Value, jetzt));
        db.PersonenSchluessel.Add(PersonenSchluessel.Anlegen(kindId, hauptschluessel.NeuenPersonenSchluesselUmhuellen(kindId), uhr));
        db.GastZugaenge.Add(GastZugang.Anlegen(
            Kennung.Neu(uhr), elternId, MailAdresse.Normalisieren(command.Mail!), Zugangslink.TokenHash(GastZugang.Zweck, token), jetzt));
        return ValueTask.FromResult(new GastAnlage(token, elternId, kindId));
    }
}

/// <summary>
/// Legt ein weiteres Kind unter 18 in der Gast-Familie an (FR-18, Geschwister nacheinander): Gast-Token gültig (nicht abgelaufen,
/// nicht übernommen). Sonst <c>null</c> ohne Änderung und <c>LinkEinloesungFehlgeschlagen</c> im Sicherheitsprotokoll.
/// </summary>
internal sealed record GastKindAnlegenCommand(string? GastToken, string? Vorname, string? Nachname, DateOnly? Geburtsdatum)
    : ICommand<Guid?>, IModulCommand<PersonenDbContext>, IBenutzerCommand
{
    /// <summary>Weder Token noch Namen ausgeben (AD-22).</summary>
    public override string ToString() => nameof(GastKindAnlegenCommand);
}

internal sealed class GastKindAnlegenValidator : AbstractValidator<GastKindAnlegenCommand>
{
    public GastKindAnlegenValidator(TimeProvider uhr) =>
        RuleFor(c => c).Custom((c, kontext) => GastRegeln.Kind(
            kontext, GastFelder.Vorname, GastFelder.Nachname, GastFelder.Geburtsdatum, c.Vorname, c.Nachname, c.Geburtsdatum, Volljaehrigkeit.Heute(uhr)));
}

internal sealed class GastKindAnlegenHandler(PersonenDbContext db, Hauptschluessel hauptschluessel, ISicherheitsprotokoll protokoll, TimeProvider uhr)
    : ICommandHandler<GastKindAnlegenCommand, Guid?>
{
    public async ValueTask<Guid?> Handle(GastKindAnlegenCommand command, CancellationToken cancellationToken)
    {
        var zugang = await GastSperre.ZugangAsync(db.GastZugaenge, command.GastToken, cancellationToken);
        if (zugang is not null)
        {
            // Dieselbe Sperre je Person wie „Kind anlegen“ und „Profil übernehmen“.
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({zugang.ElternPersonId.ToString()}, 0))", cancellationToken);
        }

        var familie = zugang is null ? default : await GastFamilie.LesenAsync(db.Elternteile, db.Personen, db.Freigaben, zugang, cancellationToken);
        if (zugang is null || familie.FamilieId is not { } familieId || !zugang.IstGueltig(uhr.GetUtcNow(), [familie.GueltigBis]))
        {
            await protokoll.SchreibenAsync(SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen, bezugId: zugang?.Id, abbruch: cancellationToken);
            return null;
        }

        var kindId = Kennung.Neu(uhr);
        db.Personen.Add(Person.Kind(kindId, familieId, command.Vorname!, command.Nachname, command.Geburtsdatum!.Value, uhr.GetUtcNow()));
        db.PersonenSchluessel.Add(PersonenSchluessel.Anlegen(kindId, hauptschluessel.NeuenPersonenSchluesselUmhuellen(kindId), uhr));
        return kindId;
    }
}

/// <summary>Lädt den Gast-Zugang per Token-Hash mit Zeilensperre (<c>FOR UPDATE</c>); <c>null</c> für ein falsch geformtes oder unbekanntes Token.</summary>
internal static class GastSperre
{
    public static async Task<GastZugang?> ZugangAsync(DbSet<GastZugang> zugaenge, string? token, CancellationToken abbruch)
    {
        if (!Zugangslink.KannTokenSein(token))
        {
            return null;
        }

        var hash = Zugangslink.TokenHash(GastZugang.Zweck, token!);
        return (await zugaenge
                .FromSql($"SELECT * FROM personen.gast_zugang WHERE \"TokenHash\" = {hash} FOR UPDATE")
                .ToListAsync(abbruch))
            .SingleOrDefault();
    }
}

/// <summary>
/// <c>POST /api/personen/oeffentlich/gaeste</c> (anonym, zugriffsbegrenzt, Antiforgery über die Dienst-Kennung der öffentlichen
/// Seiten): <c>201 {gastToken, kindPersonId}</c>; <c>400</c> je Feld.
/// </summary>
internal static class GastAnlegenEndpunkt
{
    public static async Task<Created<GastAngelegt>> Anlegen(GastAnlegen anfrage, ISender sender, CancellationToken abbruch)
    {
        var anlage = await sender.Send(
            new GastAnlegenCommand(anfrage.ElternVorname, anfrage.ElternNachname, anfrage.Mail, anfrage.KindVorname, anfrage.KindNachname, anfrage.KindGeburtsdatum),
            abbruch);
        return TypedResults.Created((string?)null, new GastAngelegt(anlage.Token, anlage.KindPersonId));
    }
}

/// <summary>
/// <c>POST /api/personen/oeffentlich/gaeste/{gastToken}/kinder</c> (anonym, zugriffsbegrenzt): <c>201 {kindPersonId}</c>; <c>400</c>
/// je Feld; Gast-Token ungültig, abgelaufen oder übernommen <c>404</c>.
/// </summary>
internal static class GastKindAnlegenEndpunkt
{
    public static async Task<Results<Created<GastKindAngelegt>, NotFound>> Anlegen(string gastToken, GastKindHinzufuegen anfrage, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new GastKindAnlegenCommand(gastToken, anfrage.Vorname, anfrage.Nachname, anfrage.Geburtsdatum), abbruch) is { } kindId
            ? TypedResults.Created((string?)null, new GastKindAngelegt(kindId))
            : TypedResults.NotFound();
}
