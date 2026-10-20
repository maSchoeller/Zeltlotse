using FluentValidation;
using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Api.Contracts.Kanaele;
using Zeltlotse.Kanaele.Daten;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Kanaele.Nachrichten;

/// <summary>
/// Story 6-1 (CAP-35, FR-61, AD-13): Sendet eine Nachricht in einen Kanal (Leitung und Team). Im Kanal „Alle“ nur mit ausdrücklicher Bestätigung
/// (<see cref="Bestaetigt"/>); der Server verlangt sie, damit kein Client ohne Bestätigung an alle sendet. Die Nachricht merkt den Push vor. Eine
/// abgeschlossene Freizeit ist nur lesbar (AD-17, Pipeline).
/// </summary>
internal sealed record NachrichtSendenCommand(Guid FreizeitId, Kanal Kanal, Guid AutorPersonId, string? Text, bool? Bestaetigt)
    : ICommand<NachrichtSendenErgebnis>, IModulCommand<KanaeleDbContext>, IBenutzerCommand, IFreizeitCommand
{
    /// <summary>Weder Person noch Text ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(NachrichtSendenCommand)}({FreizeitId}, {Kanal})";
}

/// <summary>Story 6-1: Ergebnis des Sendens.</summary>
internal abstract record NachrichtSendenErgebnis
{
    private NachrichtSendenErgebnis()
    {
    }

    public sealed record Gesendet(Nachricht Nachricht) : NachrichtSendenErgebnis;

    /// <summary>Ohne Mandant (unbekannte Freizeit): <c>404</c>.</summary>
    public sealed record NichtGefunden : NachrichtSendenErgebnis;
}

internal sealed class NachrichtSendenValidator : AbstractValidator<NachrichtSendenCommand>
{
    public const string TextFehlt = "Text fehlt.";

    public static readonly string TextZuLang = $"Höchstens {KanalFelder.TextMaximaleLaenge} Zeichen.";

    public const string NichtBestaetigt = "Eine Nachricht an alle braucht die Bestätigung „Ja, senden“.";

    public NachrichtSendenValidator()
    {
        RuleFor(c => c.Text)
            .Cascade(CascadeMode.Stop)
            .Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage(TextFehlt)
            .Must(t => t!.Trim().Length <= KanalFelder.TextMaximaleLaenge).WithMessage(TextZuLang)
            .OverridePropertyName(KanalFelder.Text);
        RuleFor(c => c.Bestaetigt)
            .Equal(true).When(c => c.Kanal == Kanal.Alle).WithMessage(NichtBestaetigt).OverridePropertyName(KanalFelder.Bestaetigt);
    }
}

internal sealed class NachrichtSendenHandler(KanaeleDbContext db, IMandantenKontext mandant, TimeProvider uhr)
    : ICommandHandler<NachrichtSendenCommand, NachrichtSendenErgebnis>
{
    public ValueTask<NachrichtSendenErgebnis> Handle(NachrichtSendenCommand command, CancellationToken cancellationToken)
    {
        if (mandant.OrganisationId is not { } organisationId)
        {
            return ValueTask.FromResult<NachrichtSendenErgebnis>(new NachrichtSendenErgebnis.NichtGefunden());
        }

        var nachricht = Nachricht.Senden(
            Kennung.Neu(uhr), organisationId, command.FreizeitId, command.Kanal, command.AutorPersonId, command.Text!.Trim(), uhr.GetUtcNow());
        db.Nachrichten.Add(nachricht);
        return ValueTask.FromResult<NachrichtSendenErgebnis>(new NachrichtSendenErgebnis.Gesendet(nachricht));
    }
}

/// <summary>
/// <c>POST /api/freizeiten/{freizeitId}/kanaele/{kanal}/nachrichten</c> (Leitung und Team): Körper <see cref="NachrichtNeu"/>. <c>201</c> mit der
/// Nachricht; Text leer oder zu lang <c>400</c> am Feld <c>text</c>; im Kanal „Alle“ ohne <c>bestaetigt: true</c> <c>400</c> am Feld <c>bestaetigt</c>;
/// unbekannter Kanal <c>404</c>; abgeschlossene Freizeit <c>409 freizeit-abgeschlossen</c>.
/// </summary>
internal static class NachrichtSendenEndpunkt
{
    public static async Task<IResult> Senden(Guid freizeitId, string kanal, NachrichtNeu koerper, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch)
    {
        if (KanalFelder.AusRoute(kanal) is not { } gewaehlt || angemeldet.PersonId is not { } person)
        {
            return TypedResults.NotFound();
        }

        return await sender.Send(new NachrichtSendenCommand(freizeitId, gewaehlt, person, koerper.Text, koerper.Bestaetigt), abbruch) switch
        {
            NachrichtSendenErgebnis.Gesendet g => TypedResults.Created((string?)null, NachrichtenLadenEndpunkt.AlsApi(g.Nachricht)),
            _ => TypedResults.NotFound(),
        };
    }
}
