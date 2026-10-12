using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Datenschutz;

namespace Zeltlotse.ArchitectureTests;

/// <summary>
/// AD-16: Jedes Modul mit Personenbezug erfüllt den Vertrag <em>Datenauskunft</em> und reagiert auf <c>PersonGeloescht</c>.
/// Personenbezug heißt: Eine Entität des schreibenden DbContexts trägt eine Eigenschaft <c>PersonId</c>. Personen selbst löst das
/// Ereignis aus und braucht keinen Empfänger. Liefert die Verstöße als lesbare Sätze; leer heißt: eingehalten.
/// </summary>
internal static class LoeschPflichtRegeln
{
    private const string PersonGeloescht = "Zeltlotse.Personen.Contracts.Events.PersonGeloescht";

    private const BindingFlags Alle = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>Die Entitäten der schreibenden DbContexts unter den Typen, die eine Eigenschaft <c>PersonId</c> tragen.</summary>
    public static IReadOnlyList<Type> EntitaetenMitPersonId(IEnumerable<Type> typen) =>
    [
        .. typen
            .Where(t => !t.IsAbstract && typeof(ModulDbContext).IsAssignableFrom(t))
            .SelectMany(t => t.GetProperties(Alle))
            .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .Select(p => p.PropertyType.GetGenericArguments()[0])
            .Where(e => e.GetProperty("PersonId", Alle) is not null)
            .Distinct(),
    ];

    public static IEnumerable<string> Verstoesse(string modul, IReadOnlyCollection<Type> typen, bool loestPersonGeloeschtAus)
    {
        if (EntitaetenMitPersonId(typen).Count == 0)
        {
            yield break;
        }

        if (!typen.Any(t => !t.IsAbstract && !t.IsInterface && typeof(IDatenauskunft).IsAssignableFrom(t)))
        {
            yield return $"{modul}: Entitäten mit PersonId, aber keine Umsetzung von IDatenauskunft.";
        }

        if (!loestPersonGeloeschtAus && !typen.Any(t => EreignisFlussTests.EmpfangeneEreignisse(t).Any(e => e.FullName == PersonGeloescht)))
        {
            yield return $"{modul}: Entitäten mit PersonId, aber kein Empfänger für PersonGeloescht.";
        }
    }
}
