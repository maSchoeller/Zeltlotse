using Zeltlotse.SharedKernel.Daten;

namespace Zeltlotse.SharedKernel.Commands;

/// <summary>Nicht-generisches Merkmal für die Pipeline-Stufen; Commands tragen immer <see cref="IModulCommand{TDbContext}"/>.</summary>
public interface IModulCommandBasis;

/// <summary>
/// Jeder Command trägt den schreibenden DbContext des eigenen Moduls (AD-27).
/// Auf ihm öffnet die Pipeline die Transaktion und schreibt die Outbox.
/// </summary>
public interface IModulCommand<TDbContext> : IModulCommandBasis
    where TDbContext : ModulDbContext;

/// <summary>Merkmal für Commands, die ein Endpunkt auslöst (AD-17, AD-20). Ereignis-Empfänger tragen es nie.</summary>
public interface IBenutzerCommand;

/// <summary>
/// Merkmal für Benutzer-Commands, die zu einem Funktions-Schalter der Freizeit gehören (AD-20): Ist er aus, lehnt die Pipeline den Command
/// ab (<c>404</c>). Gilt nur zusammen mit <see cref="IBenutzerCommand"/>; Ereignis-Empfänger laufen immer.
/// </summary>
public interface ISchalterCommand
{
    Guid FreizeitId { get; }

    Rechte.FunktionsSchalter Schalter { get; }
}
