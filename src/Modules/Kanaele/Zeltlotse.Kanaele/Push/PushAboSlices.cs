using System.Buffers.Text;
using FluentValidation;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Kanaele;
using Zeltlotse.Kanaele.Daten;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Push;

namespace Zeltlotse.Kanaele.Push;

/// <summary>
/// Story 6-1 (AD-21): Speichert das Push-Abo des Browsers für die Freizeit (Leitung und Team). Je Freizeit und Endpunkt gibt es ein Abo; ein erneutes Senden
/// überschreibt es (auch von einer anderen Person am selben Gerät). Als Endpunkt gilt nur <c>https</c> zu einem bekannten Push-Dienst (Schutz gegen SSRF).
/// Eine abgeschlossene Freizeit ist nur lesbar (AD-17, Pipeline).
/// </summary>
internal sealed record PushAboSpeichern(Guid FreizeitId, Guid PersonId, string? Endpunkt, string? P256dh, string? Auth)
    : ICommand<bool>, IModulCommand<KanaeleDbContext>, IBenutzerCommand, IFreizeitCommand
{
    /// <summary>Weder Person noch Endpunkt oder Schlüssel ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(PushAboSpeichern)}({FreizeitId})";
}

internal sealed class PushAboSpeichernValidator : AbstractValidator<PushAboSpeichern>
{
    public const string EndpunktFehler = "Nur die Adresse eines bekannten Push-Dienstes (https).";

    public const string SchluesselFehler = "Kein gültiger Schlüssel des Browsers.";

    public PushAboSpeichernValidator()
    {
        RuleFor(c => c.Endpunkt).Must(PushEndpunkte.IstErlaubt).WithMessage(EndpunktFehler).OverridePropertyName(KanalFelder.Endpunkt);
        RuleFor(c => c.P256dh).Must(s => PushSchluesselPruefung.Laenge(s) == 65).WithMessage(SchluesselFehler).OverridePropertyName(KanalFelder.P256dh);
        RuleFor(c => c.Auth).Must(s => PushSchluesselPruefung.Laenge(s) == 16).WithMessage(SchluesselFehler).OverridePropertyName(KanalFelder.Auth);
    }
}

/// <summary>Story 6-1: Prüft die Schlüssel des Browsers (base64url, ohne Füllzeichen oder mit).</summary>
internal static class PushSchluesselPruefung
{
    /// <summary>Die Zahl der Bytes eines base64url-Werts; <c>-1</c>, wenn er fehlt, zu lang oder nicht lesbar ist.</summary>
    public static int Laenge(string? wert)
    {
        if (string.IsNullOrEmpty(wert) || wert.Length > KanaeleModell.SchluesselMaximaleLaenge)
        {
            return -1;
        }

        var puffer = new byte[Base64Url.GetMaxDecodedLength(wert.Length)];
        try
        {
            return Base64Url.TryDecodeFromChars(wert.TrimEnd('='), puffer, out var laenge) ? laenge : -1;
        }
        catch (FormatException)
        {
            return -1;
        }
    }
}

internal sealed class PushAboSpeichernHandler(KanaeleDbContext db, IMandantenKontext mandant, TimeProvider uhr) : ICommandHandler<PushAboSpeichern, bool>
{
    public async ValueTask<bool> Handle(PushAboSpeichern command, CancellationToken cancellationToken)
    {
        if (mandant.OrganisationId is not { } organisationId)
        {
            return false;
        }

        await db.AboSpeichernAsync(
            Kennung.Neu(uhr), organisationId, command.FreizeitId, command.PersonId, command.Endpunkt!, command.P256dh!, command.Auth!, uhr.GetUtcNow(),
            cancellationToken);
        return true;
    }
}

/// <summary>
/// Story 6-1: Löscht die eigenen Push-Abos zu einem Endpunkt („Benachrichtigungen ausschalten“): Der Browser beendet sein Abo für das ganze Gerät, darum
/// gehen die Abos dieses Endpunkts in allen Freizeiten der Organisation (Mandantenfilter). Ein unbekanntes oder fremdes Abo ändert nichts.
/// </summary>
internal sealed record PushAboAbmelden(Guid FreizeitId, Guid PersonId, string? Endpunkt)
    : ICommand, IModulCommand<KanaeleDbContext>, IBenutzerCommand, IFreizeitCommand
{
    /// <summary>Weder Person noch Endpunkt ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(PushAboAbmelden)}({FreizeitId})";
}

internal sealed class PushAboAbmeldenHandler(KanaeleDbContext db) : ICommandHandler<PushAboAbmelden>
{
    public async ValueTask<Unit> Handle(PushAboAbmelden command, CancellationToken cancellationToken)
    {
        await db.PushAbos
            .Where(a => a.PersonId == command.PersonId && a.Endpunkt == command.Endpunkt)
            .ExecuteDeleteAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>
/// Story 6-1: Löscht Abos, die der Push-Dienst nicht mehr kennt (404/410). Die Zustellung der Outbox schickt diesen Command; kein Benutzer-Command, er läuft
/// also auch nach dem Abschluss. Unter dem Mandantenfilter aus dem Umschlag.
/// </summary>
internal sealed record PushAbosEntfernen(IReadOnlyList<Guid> AboIds) : ICommand, IModulCommand<KanaeleDbContext>;

internal sealed class PushAbosEntfernenHandler(KanaeleDbContext db) : ICommandHandler<PushAbosEntfernen>
{
    public async ValueTask<Unit> Handle(PushAbosEntfernen command, CancellationToken cancellationToken)
    {
        var ids = command.AboIds.ToList();
        await db.PushAbos.Where(a => ids.Contains(a.Id)).ExecuteDeleteAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>
/// Endpunkte des Push-Abos unter <c>/api/freizeiten/{freizeitId}/kanaele/push</c> (Leitung und Team, Story 6-1): <c>GET schluessel</c> liefert den
/// öffentlichen VAPID-Schlüssel; <c>POST abo</c> speichert das Abo (<c>204</c>, Endpunkt oder Schlüssel ungültig <c>400</c> am Feld); <c>POST abo/abmelden</c>
/// löscht das eigene Abo (<c>204</c>, auch wenn es keines gibt). Beide POSTs nach dem Abschluss <c>409 freizeit-abgeschlossen</c>.
/// </summary>
internal static class PushAboEndpunkte
{
    public static IResult Schluessel(Guid freizeitId, VapidSchluessel vapid) => TypedResults.Ok(new PushSchluessel(vapid.Oeffentlich));

    public static async Task<IResult> Speichern(Guid freizeitId, PushAboNeu koerper, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch)
    {
        if (angemeldet.PersonId is not { } person)
        {
            return TypedResults.NotFound();
        }

        return await sender.Send(new PushAboSpeichern(freizeitId, person, koerper.Endpunkt?.Trim(), koerper.P256dh?.Trim(), koerper.Auth?.Trim()), abbruch)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    public static async Task<IResult> Abmelden(Guid freizeitId, PushAbmelden koerper, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch)
    {
        if (angemeldet.PersonId is not { } person)
        {
            return TypedResults.NotFound();
        }

        await sender.Send(new PushAboAbmelden(freizeitId, person, koerper.Endpunkt?.Trim()), abbruch);
        return TypedResults.NoContent();
    }
}
