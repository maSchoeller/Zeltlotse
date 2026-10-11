using Mediator;
using Zeltlotse.Konten.Daten;
using Zeltlotse.Konten.Domaene;
using Zeltlotse.Konten.Sitzungen;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;

namespace Zeltlotse.Konten.Anmelden;

/// <summary>
/// Löst das Token eines Login-Links ein. <c>null</c>: unbekannt, abgelaufen, schon benutzt oder verworfen; dann steht
/// <c>LinkEinloesungFehlgeschlagen</c> im Sicherheitsprotokoll (eigene Transaktion, bleibt auch bei einem späteren Fehler).
/// </summary>
internal sealed record LoginLinkEinloesenCommand(string? Token)
    : ICommand<SitzungsDaten?>, IModulCommand<KontenDbContext>, IBenutzerCommand
{
    public override string ToString() => nameof(LoginLinkEinloesenCommand);
}

internal sealed class LoginLinkEinloesenHandler(KontenDbContext db, TimeProvider uhr, ISicherheitsprotokoll protokoll)
    : ICommandHandler<LoginLinkEinloesenCommand, SitzungsDaten?>
{
    public async ValueTask<SitzungsDaten?> Handle(LoginLinkEinloesenCommand command, CancellationToken cancellationToken)
    {
        LoginAnfrage? anfrage = null;
        if (Geheimnisse.KannTokenSein(command.Token))
        {
            var hash = Geheimnisse.TokenHash(Geheimnisse.LoginZweck, command.Token!);
            anfrage = await AnfrageSperre.PerTokenHashAsync(db.LoginAnfragen, hash, cancellationToken);
        }

        if (anfrage is null || !anfrage.IstEinloesbar(uhr.GetUtcNow()))
        {
            await protokoll.SchreibenAsync(SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen, bezugId: anfrage?.Id, abbruch: cancellationToken);
            return null;
        }

        return await KontoZurAnfrage.EinloesenAsync(db.Konten, anfrage, uhr, cancellationToken);
    }
}
