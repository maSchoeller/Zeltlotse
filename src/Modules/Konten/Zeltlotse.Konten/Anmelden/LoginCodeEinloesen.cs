using Mediator;
using Zeltlotse.Konten.Daten;
using Zeltlotse.Konten.Domaene;
using Zeltlotse.Konten.Sitzungen;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;

namespace Zeltlotse.Konten.Anmelden;

/// <summary>
/// Löst den Login-Code einer Anfrage ein (installierte App). Ein falscher Code zählt als Fehlversuch; nach fünf ist die
/// Anfrage verworfen. <c>null</c>: falsch, abgelaufen, benutzt oder verworfen; dann steht
/// <c>LinkEinloesungFehlgeschlagen</c> im Sicherheitsprotokoll.
/// </summary>
internal sealed record LoginCodeEinloesenCommand(Guid AnfrageId, string? Code)
    : ICommand<SitzungsDaten?>, IModulCommand<KontenDbContext>, IBenutzerCommand
{
    public override string ToString() => $"{nameof(LoginCodeEinloesenCommand)}({AnfrageId})";
}

internal sealed class LoginCodeEinloesenHandler(KontenDbContext db, TimeProvider uhr, ISicherheitsprotokoll protokoll)
    : ICommandHandler<LoginCodeEinloesenCommand, SitzungsDaten?>
{
    public async ValueTask<SitzungsDaten?> Handle(LoginCodeEinloesenCommand command, CancellationToken cancellationToken)
    {
        var anfrage = await AnfrageSperre.PerIdAsync(db.LoginAnfragen, command.AnfrageId, cancellationToken);
        if (anfrage is null || !anfrage.IstEinloesbar(uhr.GetUtcNow()))
        {
            await protokoll.SchreibenAsync(SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen, bezugId: anfrage?.Id, abbruch: cancellationToken);
            return null;
        }

        if (!Geheimnisse.CodePasst(anfrage, command.Code))
        {
            await protokoll.SchreibenAsync(SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen, bezugId: anfrage.Id, abbruch: cancellationToken);
            anfrage.FehlversuchZaehlen();
            return null;
        }

        return await KontoZurAnfrage.EinloesenAsync(db.Konten, anfrage, uhr, cancellationToken);
    }
}
