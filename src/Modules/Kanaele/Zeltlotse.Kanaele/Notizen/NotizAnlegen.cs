using FluentValidation;
using Mediator;
using Microsoft.AspNetCore.Http;
using Zeltlotse.Api.Contracts.Kanaele;
using Zeltlotse.Kanaele.Daten;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Kanaele.Notizen;

/// <summary>
/// Story 6-3 (CAP-37, FR-63): Legt einen Punkt auf dem Ansagen-Zettel oder der Besprechungsliste an (Leitung und Team), nur mit Text. Eine abgeschlossene
/// Freizeit ist nur lesbar (AD-17, Pipeline).
/// </summary>
internal sealed record NotizAnlegenCommand(Guid FreizeitId, NotizArt Art, string? Text)
    : ICommand<NotizAnlegenErgebnis>, IModulCommand<KanaeleDbContext>, IBenutzerCommand, IFreizeitCommand
{
    /// <summary>Keinen Text ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(NotizAnlegenCommand)}({FreizeitId}, {Art})";
}

/// <summary>Story 6-3: Ergebnis des Anlegens.</summary>
internal abstract record NotizAnlegenErgebnis
{
    private NotizAnlegenErgebnis()
    {
    }

    public sealed record Angelegt(Notiz Notiz) : NotizAnlegenErgebnis;

    /// <summary>Ohne Mandant (unbekannte Freizeit): <c>404</c>.</summary>
    public sealed record NichtGefunden : NotizAnlegenErgebnis;
}

internal sealed class NotizAnlegenValidator : AbstractValidator<NotizAnlegenCommand>
{
    public const string TextFehlt = "Text fehlt.";

    public static readonly string TextZuLang = $"Höchstens {KanalFelder.NotizTextMaximaleLaenge} Zeichen.";

    public NotizAnlegenValidator() =>
        RuleFor(c => c.Text)
            .Cascade(CascadeMode.Stop)
            .Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage(TextFehlt)
            .Must(t => t!.Trim().Length <= KanalFelder.NotizTextMaximaleLaenge).WithMessage(TextZuLang)
            .OverridePropertyName(KanalFelder.Text);
}

internal sealed class NotizAnlegenHandler(KanaeleDbContext db, IMandantenKontext mandant, TimeProvider uhr)
    : ICommandHandler<NotizAnlegenCommand, NotizAnlegenErgebnis>
{
    public ValueTask<NotizAnlegenErgebnis> Handle(NotizAnlegenCommand command, CancellationToken cancellationToken)
    {
        if (mandant.OrganisationId is not { } organisationId)
        {
            return ValueTask.FromResult<NotizAnlegenErgebnis>(new NotizAnlegenErgebnis.NichtGefunden());
        }

        var notiz = Notiz.Anlegen(Kennung.Neu(uhr), organisationId, command.FreizeitId, command.Art, command.Text!.Trim(), uhr.GetUtcNow());
        db.Notizen.Add(notiz);
        return ValueTask.FromResult<NotizAnlegenErgebnis>(new NotizAnlegenErgebnis.Angelegt(notiz));
    }
}

/// <summary>
/// <c>POST /api/freizeiten/{freizeitId}/kanaele/notizen/{art}</c> (Leitung und Team): Körper <see cref="NotizNeu"/>. <c>201</c> mit dem Punkt; Text leer oder
/// zu lang <c>400</c> am Feld <c>text</c>; unbekannte Art <c>404</c>; abgeschlossene Freizeit <c>409 freizeit-abgeschlossen</c>.
/// </summary>
internal static class NotizAnlegenEndpunkt
{
    public static async Task<IResult> Anlegen(Guid freizeitId, string art, NotizNeu koerper, ISender sender, CancellationToken abbruch)
    {
        if (KanalFelder.NotizArtAusRoute(art) is not { } gewaehlt)
        {
            return TypedResults.NotFound();
        }

        return await sender.Send(new NotizAnlegenCommand(freizeitId, gewaehlt, koerper.Text), abbruch) switch
        {
            NotizAnlegenErgebnis.Angelegt a => TypedResults.Created((string?)null, NotizenLadenEndpunkt.AlsApi(a.Notiz)),
            _ => TypedResults.NotFound(),
        };
    }
}
