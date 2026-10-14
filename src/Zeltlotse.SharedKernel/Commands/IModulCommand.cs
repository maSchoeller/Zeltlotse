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
/// Story 3-8: Merkmal für Commands mit Bezug zu genau einer Freizeit (AD-17). Ist die Freizeit gelaufen, lehnt die Pipeline jeden
/// <see cref="IBenutzerCommand"/> mit diesem Merkmal ab (<c>409 freizeit-abgeschlossen</c>), außer er trägt
/// <see cref="ErlaubtNachAbschlussAttribute"/>. <see cref="Guid.Empty"/> heißt: ohne Freizeit-Bezug (z. B. eine Einsicht im
/// Org-Kontext). Ein Architekturtest verlangt das Merkmal an jedem Benutzer-Command mit einer Eigenschaft <c>Guid FreizeitId</c>.
/// </summary>
public interface IFreizeitCommand
{
    Guid FreizeitId { get; }
}

/// <summary>
/// Merkmal für Benutzer-Commands, die zu einem Funktions-Schalter der Freizeit gehören (AD-20): Ist er aus, lehnt die Pipeline den Command
/// ab (<c>404</c>). Gilt nur zusammen mit <see cref="IBenutzerCommand"/>; Ereignis-Empfänger laufen immer.
/// </summary>
public interface ISchalterCommand : IFreizeitCommand
{
    Rechte.FunktionsSchalter Schalter { get; }
}

/// <summary>
/// Story 3-8 (AD-17): Dieser Benutzer-Command bleibt auch nach dem Abschluss der Freizeit erlaubt. Nur für Anschaffungswünsche in Inventar
/// (Schäden und Verluste eintragen, bearbeiten, entfernen, erledigt) und Erstattungsvermerke an Belegen in Finanzen.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ErlaubtNachAbschlussAttribute : Attribute;
