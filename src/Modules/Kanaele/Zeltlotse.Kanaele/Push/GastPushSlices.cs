using FluentValidation;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Kanaele;
using Zeltlotse.Kanaele.Daten;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Push;
using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Kanaele.Push;

/// <summary>
/// Story 6-4 (AD-12, AD-21): Speichert das Push-Abo des Browsers eines QR-Gasts für den Kanal „Alle“, die einzige schreibende Aktion eines Gasts. Gleiche
/// Prüfung wie bei den Team-Abos: nur <c>https</c> zu einem bekannten Push-Dienst (Schutz gegen SSRF), Schlüssel des Browsers mit 65 und 16 Bytes. Je
/// Freizeit und Endpunkt ein Abo; erneutes Senden überschreibt es. Gehört zum Modul Teilnehmer-Zugang; eine abgeschlossene Freizeit ist nur lesbar.
/// </summary>
internal sealed record GastPushAboSpeichern(Guid FreizeitId, Guid GeraeteId, Guid Stempel, string? Endpunkt, string? P256dh, string? Auth)
    : ICommand<bool>, IModulCommand<KanaeleDbContext>, IBenutzerCommand, ISchalterCommand
{
    public FunktionsSchalter Schalter => FunktionsSchalter.TeilnehmerZugang;

    /// <summary>Weder Gerät noch Endpunkt oder Schlüssel ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(GastPushAboSpeichern)}({FreizeitId})";
}

internal sealed class GastPushAboSpeichernValidator : AbstractValidator<GastPushAboSpeichern>
{
    public GastPushAboSpeichernValidator()
    {
        RuleFor(c => c.Endpunkt).Must(PushEndpunkte.IstErlaubt).WithMessage(PushAboSpeichernValidator.EndpunktFehler).OverridePropertyName(KanalFelder.Endpunkt);
        RuleFor(c => c.P256dh).Must(s => PushSchluesselPruefung.Laenge(s) == 65).WithMessage(PushAboSpeichernValidator.SchluesselFehler)
            .OverridePropertyName(KanalFelder.P256dh);
        RuleFor(c => c.Auth).Must(s => PushSchluesselPruefung.Laenge(s) == 16).WithMessage(PushAboSpeichernValidator.SchluesselFehler)
            .OverridePropertyName(KanalFelder.Auth);
    }
}

internal sealed class GastPushAboSpeichernHandler(KanaeleDbContext db, IMandantenKontext mandant, TimeProvider uhr) : ICommandHandler<GastPushAboSpeichern, bool>
{
    public async ValueTask<bool> Handle(GastPushAboSpeichern command, CancellationToken cancellationToken)
    {
        if (mandant.OrganisationId is not { } organisationId)
        {
            return false;
        }

        await db.GastAboSpeichernAsync(
            Kennung.Neu(uhr), organisationId, command.FreizeitId, command.GeraeteId, command.Stempel, command.Endpunkt!, command.P256dh!, command.Auth!, uhr.GetUtcNow(),
            cancellationToken);
        return true;
    }
}

/// <summary>Story 6-4: Löscht das Gast-Abo dieses Geräts zu einem Endpunkt („Benachrichtigungen ausschalten“); ein unbekanntes oder fremdes Abo ändert nichts.</summary>
internal sealed record GastPushAboAbmelden(Guid FreizeitId, Guid GeraeteId, string? Endpunkt)
    : ICommand, IModulCommand<KanaeleDbContext>, IBenutzerCommand, ISchalterCommand
{
    public FunktionsSchalter Schalter => FunktionsSchalter.TeilnehmerZugang;

    /// <summary>Weder Gerät noch Endpunkt ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(GastPushAboAbmelden)}({FreizeitId})";
}

internal sealed class GastPushAboAbmeldenHandler(KanaeleDbContext db) : ICommandHandler<GastPushAboAbmelden>
{
    public async ValueTask<Unit> Handle(GastPushAboAbmelden command, CancellationToken cancellationToken)
    {
        await db.GastPushAbos
            .Where(a => a.FreizeitId == command.FreizeitId && a.GeraeteId == command.GeraeteId && a.Endpunkt == command.Endpunkt)
            .ExecuteDeleteAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>
/// Story 6-4: Löscht Gast-Abos, die der Push-Dienst nicht mehr kennt (404/410). Die Zustellung der Outbox schickt diesen Command; kein Benutzer-Command.
/// Unter dem Mandantenfilter aus dem Umschlag.
/// </summary>
internal sealed record GastPushAbosEntfernen(IReadOnlyList<Guid> AboIds) : ICommand, IModulCommand<KanaeleDbContext>;

internal sealed class GastPushAbosEntfernenHandler(KanaeleDbContext db) : ICommandHandler<GastPushAbosEntfernen>
{
    public async ValueTask<Unit> Handle(GastPushAbosEntfernen command, CancellationToken cancellationToken)
    {
        var ids = command.AboIds.ToList();
        await db.GastPushAbos.Where(a => ids.Contains(a.Id)).ExecuteDeleteAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>
/// Endpunkte des Gast-Abos unter <c>/api/teilnehmer/push</c> (QR-Gast, Story 6-4): <c>GET schluessel</c> liefert den öffentlichen VAPID-Schlüssel;
/// <c>POST abo</c> speichert das Abo (<c>204</c>, Endpunkt oder Schlüssel ungültig <c>400</c> am Feld); <c>POST abo/abmelden</c> löscht das Abo dieses
/// Geräts (<c>204</c>, auch wenn es keines gibt). Freizeit und Gerät kommen aus der geprüften Gast-Sitzung, nie aus der Adresse.
/// </summary>
internal static class GastPushEndpunkte
{
    public static IResult Schluessel(VapidSchluessel vapid) => TypedResults.Ok(new PushSchluessel(vapid.Oeffentlich));

    public static async Task<IResult> Speichern(PushAboNeu koerper, IQrGast gast, ISender sender, CancellationToken abbruch)
    {
        if (gast.FreizeitId is not { } freizeit || gast.GeraeteId is not { } geraet || gast.Stempel is not { } stempel)
        {
            return TypedResults.NotFound();
        }

        return await sender.Send(new GastPushAboSpeichern(freizeit, geraet, stempel, koerper.Endpunkt?.Trim(), koerper.P256dh?.Trim(), koerper.Auth?.Trim()), abbruch)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    public static async Task<IResult> Abmelden(PushAbmelden koerper, IQrGast gast, ISender sender, CancellationToken abbruch)
    {
        if (gast.FreizeitId is not { } freizeit || gast.GeraeteId is not { } geraet)
        {
            return TypedResults.NotFound();
        }

        await sender.Send(new GastPushAboAbmelden(freizeit, geraet, koerper.Endpunkt?.Trim()), abbruch);
        return TypedResults.NoContent();
    }
}
