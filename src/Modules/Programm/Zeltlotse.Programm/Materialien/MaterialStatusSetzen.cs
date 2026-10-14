using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Programm;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Programm.Daten;
using Zeltlotse.Programm.Programme;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Commands;
using Programmpunkt = Zeltlotse.Programm.Programme.Programmpunkt;

namespace Zeltlotse.Programm.Materialien;

/// <summary>
/// Story 3-2: Statuswechsel eines Materials (FR-36, AD-28) in den gewählten Zustand; jeder Zustand ist von jedem aus erlaubt. Gleichzeitige
/// Wechsel laufen über die Zeilensperre nacheinander, der letzte gewinnt. Eine abgeschlossene Freizeit ist nur lesbar (AD-17).
/// </summary>
internal sealed record MaterialStatusSetzenCommand(Guid FreizeitId, Guid MaterialId, MaterialStatus? Status)
    : ICommand<MaterialUebergang>, IModulCommand<ProgrammDbContext>, IBenutzerCommand, IFreizeitCommand;

/// <summary>Ergebnis eines Übergangs am Material; bei <see cref="Erfolg"/> das verfolgte Material (nach dem Speichern mit neuer Version) und sein Punkt.</summary>
internal abstract record MaterialUebergang
{
    private MaterialUebergang()
    {
    }

    public sealed record Erfolg(Material Material, Programmpunkt Punkt) : MaterialUebergang;

    /// <summary>Eine andere Person bringt das Material mit.</summary>
    public sealed record Konflikt : MaterialUebergang;

    public sealed record Abgeschlossen : MaterialUebergang;

    public sealed record NichtGefunden : MaterialUebergang;
}

internal sealed class MaterialStatusSetzenValidator : AbstractValidator<MaterialStatusSetzenCommand>
{
    public const string StatusFehlt = "Status fehlt: Gebraucht, Vorhanden, Besorgt oder Eingepackt.";

    public MaterialStatusSetzenValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (c.Status is not { } status || !Enum.IsDefined(status))
            {
                kontext.AddFailure(new ValidationFailure(MaterialFelder.Status, StatusFehlt));
            }
        });
}

internal sealed class MaterialStatusSetzenHandler(ProgrammDbContext db, IFreizeitZeitpunkte zeitpunkte)
    : ICommandHandler<MaterialStatusSetzenCommand, MaterialUebergang>
{
    public async ValueTask<MaterialUebergang> Handle(MaterialStatusSetzenCommand command, CancellationToken cancellationToken)
    {
        if (!(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return new MaterialUebergang.NichtGefunden();
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return new MaterialUebergang.Abgeschlossen();
        }

        var material = await db.MaterialSperrenAsync(command.FreizeitId, command.MaterialId, cancellationToken);
        if (material is null)
        {
            return new MaterialUebergang.NichtGefunden();
        }

        material.StatusSetzen(command.Status!.Value);
        return new MaterialUebergang.Erfolg(material, await db.Programmpunkte.SingleAsync(p => p.Id == material.ProgrammpunktId, cancellationToken));
    }
}

/// <summary>Gemeinsame Antwort der Übergänge am Material.</summary>
internal static class MaterialUebergaenge
{
    /// <summary>Antwort eines Übergangs: <c>200</c> mit dem Material, <c>409</c> mit Problemcode oder <c>404</c>.</summary>
    public static IResult Antwort(MaterialUebergang ergebnis, Guid? ich) => ergebnis switch
    {
        MaterialUebergang.Erfolg erfolg => TypedResults.Ok(MaterialLesen.AlsApi(erfolg.Material, erfolg.Punkt, ich)),
        MaterialUebergang.Konflikt => ProgrammProbleme.Konflikt("Jemand anderes bringt dieses Material mit."),
        MaterialUebergang.Abgeschlossen => ProgrammProbleme.Abgeschlossen(),
        _ => TypedResults.NotFound(),
    };
}

/// <summary>
/// <c>POST /api/freizeiten/{freizeitId}/programm/material/{materialId}/status</c> (Leitung und Team, FR-36): Körper
/// <c>{ "status": "Gebraucht"|"Vorhanden"|"Besorgt"|"Eingepackt" }</c>. <c>200</c> mit dem Material (neuer Status, neue Version); Status fehlt
/// oder unbekannt <c>400</c> am Feld <c>status</c>; abgeschlossene Freizeit <c>409 freizeit-abgeschlossen</c>; unbekanntes Material oder
/// Material einer anderen Freizeit <c>404</c>.
/// </summary>
internal static class MaterialStatusSetzenEndpunkt
{
    public static async Task<IResult> Setzen(Guid freizeitId, Guid materialId, JsonElement koerper, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch) =>
        MaterialUebergaenge.Antwort(await sender.Send(new MaterialStatusSetzenCommand(freizeitId, materialId, StatusLesen(koerper)), abbruch), angemeldet.PersonId);

    /// <summary>Der Status als Name; ein unbekannter Name, eine Zahl oder ein fehlender Status ist ein Fehler am Feld <c>status</c>.</summary>
    private static MaterialStatus? StatusLesen(JsonElement koerper)
    {
        if (koerper.ValueKind != JsonValueKind.Object || !koerper.TryGetProperty(MaterialFelder.Status, out var wert) || wert.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var name = wert.ValueKind == JsonValueKind.String ? wert.GetString() : null;
        return Enum.GetValues<MaterialStatus>().Where(s => s.ToString() == name).Select(s => (MaterialStatus?)s).SingleOrDefault()
               ?? throw FeldPatch.Fehler(MaterialFelder.Status, MaterialStatusSetzenValidator.StatusFehlt);
    }
}
