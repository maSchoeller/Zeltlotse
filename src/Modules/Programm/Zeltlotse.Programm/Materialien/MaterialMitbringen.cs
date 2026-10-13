using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Programm.Daten;
using Zeltlotse.SharedKernel.Anmeldung;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.Programm.Materialien;

/// <summary>
/// Story 3-2: „Ich bringe das mit“ oder „Doch nicht mitbringen“ (AD-28), nur für sich selbst; höchstens eine Person bringt ein Material mit.
/// Wiederholt ist beides unschädlich; bringt es eine andere Person mit, ist das ein Konflikt. Die Zeilensperre lässt zwei gleichzeitige Versuche
/// nacheinander laufen: genau einer gewinnt. Der Status bleibt dabei, wie er ist. Eine abgeschlossene Freizeit ist nur lesbar (AD-17).
/// </summary>
internal sealed record MaterialMitbringenCommand(Guid FreizeitId, Guid MaterialId, Guid PersonId, bool Mitbringen)
    : ICommand<MaterialUebergang>, IModulCommand<ProgrammDbContext>, IBenutzerCommand
{
    /// <summary>Keine Personenkennung ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(MaterialMitbringenCommand)}({FreizeitId}, {MaterialId}, {Mitbringen})";
}

internal sealed class MaterialMitbringenHandler(ProgrammDbContext db, IFreizeitZeitpunkte zeitpunkte)
    : ICommandHandler<MaterialMitbringenCommand, MaterialUebergang>
{
    public async ValueTask<MaterialUebergang> Handle(MaterialMitbringenCommand command, CancellationToken cancellationToken)
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

        var erlaubt = command.Mitbringen ? material.Mitbringen(command.PersonId) : material.NichtMitbringen(command.PersonId);
        return erlaubt
            ? new MaterialUebergang.Erfolg(material, await db.Programmpunkte.SingleAsync(p => p.Id == material.ProgrammpunktId, cancellationToken))
            : new MaterialUebergang.Konflikt();
    }
}

/// <summary>
/// <c>POST /api/freizeiten/{freizeitId}/programm/material/{materialId}/mitbringen</c> und <c>…/nicht-mitbringen</c> (Leitung und Team, FR-36):
/// <c>200</c> mit dem Material, auch wiederholt; bringt es eine andere Person mit <c>409 konflikt</c>; abgeschlossene Freizeit
/// <c>409 freizeit-abgeschlossen</c>; unbekanntes Material oder Material einer anderen Freizeit <c>404</c>.
/// </summary>
internal static class MaterialMitbringenEndpunkt
{
    public static Task<IResult> Mitbringen(Guid freizeitId, Guid materialId, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch) =>
        AusfuehrenAsync(freizeitId, materialId, mitbringen: true, angemeldet, sender, abbruch);

    public static Task<IResult> NichtMitbringen(Guid freizeitId, Guid materialId, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch) =>
        AusfuehrenAsync(freizeitId, materialId, mitbringen: false, angemeldet, sender, abbruch);

    private static async Task<IResult> AusfuehrenAsync(
        Guid freizeitId, Guid materialId, bool mitbringen, IAngemeldet angemeldet, ISender sender, CancellationToken abbruch)
    {
        var ich = angemeldet.PersonId ?? throw new InvalidOperationException("Ohne Sitzung erreicht niemand diesen Endpunkt.");
        return MaterialUebergaenge.Antwort(await sender.Send(new MaterialMitbringenCommand(freizeitId, materialId, ich, mitbringen), abbruch), ich);
    }
}
