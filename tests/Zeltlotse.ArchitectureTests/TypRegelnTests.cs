using System.Reflection;
using FluentValidation;
using Mediator;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.ArchitectureTests;

/// <summary>Regeln an Typen der Module (AD-4, AD-27): Sichtbarkeit, Command-Merkmal, öffentliche Oberfläche, Ereignis-Inhalt.</summary>
public sealed class TypRegelnTests
{
    private static IEnumerable<Assembly> ModulUndTestmodul =>
        Loesung.ModulAssemblies.Append(Loesung.AssemblyVon("Zeltlotse.Testmodul")!);

    private static IEnumerable<Type> AlleTypen(Assembly assembly) =>
        assembly.GetTypes().Where(t => !t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false));

    [Fact]
    public void Commands_Handler_und_Validatoren_sind_internal()
    {
        var oeffentliche = ModulUndTestmodul
            .SelectMany(AlleTypen)
            .Where(t => t.IsVisible && (IstCommand(t) || IstCommandHandler(t) || IstValidator(t)))
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(oeffentliche);
    }

    [Fact]
    public void Jeder_Command_traegt_IModulCommand_mit_dem_DbContext_des_eigenen_Moduls()
    {
        var verstoesse = ModulUndTestmodul
            .SelectMany(AlleTypen)
            .Where(IstCommand)
            .Where(t =>
            {
                var merkmal = t.GetInterfaces().SingleOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IModulCommand<>));
                return merkmal is null || merkmal.GetGenericArguments()[0].Assembly != t.Assembly;
            })
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(verstoesse);
    }

    [Fact]
    public void Testmodul_enthaelt_Commands_damit_die_Regeln_greifen()
    {
        Assert.Contains(AlleTypen(Loesung.AssemblyVon("Zeltlotse.Testmodul")!), IstCommand);
        Assert.Contains(AlleTypen(Loesung.AssemblyVon("Zeltlotse.Testmodul")!), IstCommandHandler);
        Assert.Contains(AlleTypen(Loesung.AssemblyVon("Zeltlotse.Testmodul")!), IstValidator);
    }

    [Fact]
    public void Module_veroeffentlichen_nur_Add_Modul_und_Map_Endpoints()
    {
        foreach (var modul in Loesung.Module)
        {
            var assembly = Loesung.AssemblyVon(modul.Projekt)!;
            var oeffentlich = assembly.GetExportedTypes();

            var registrierung = Assert.Single(oeffentlich);
            Assert.Equal($"{modul.Projekt}.{modul.Name}Modul", registrierung.FullName);
            Assert.True(registrierung.IsAbstract && registrierung.IsSealed, $"{registrierung} muss statisch sein.");
            var methoden = registrierung.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(m => m.Name)
                .Order();
            Assert.Equal([$"Add{modul.Name}Modul", $"Map{modul.Name}Endpoints"], methoden);
        }
    }

    [Fact]
    public void Ereignis_Typen_bestehen_nur_aus_Guid_Zahlen_Datumswerten_Enums_und_eigenen_Event_Typen()
    {
        var verstoesse = new List<string>();
        var eventTypen = Loesung.Module
            .Select(m => (Modul: m, Assembly: Loesung.AssemblyVon(m.ContractsProjekt)!))
            .SelectMany(x => AlleTypen(x.Assembly).Where(t => t.Namespace == x.Modul.EventsNamensraum))
            .Concat(AlleTypen(Loesung.AssemblyVon("Zeltlotse.Testmodul")!).Where(t => t.Namespace == "Zeltlotse.Testmodul.Events"));

        foreach (var typ in eventTypen.Where(t => !t.IsEnum))
        {
            var mitglieder = typ.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => (p.Name, Typ: p.PropertyType))
                .Concat(typ.GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => (f.Name, Typ: f.FieldType)));
            foreach (var (name, mitgliedTyp) in mitglieder)
            {
                if (!IstErlaubterEreignisWert(mitgliedTyp, typ.Namespace!))
                {
                    verstoesse.Add($"{typ.FullName}.{name}: {mitgliedTyp}");
                }
            }
        }

        Assert.Empty(verstoesse);
    }

    [Fact]
    public void Ereignis_Wert_Regel_unterscheidet_erlaubte_und_verbotene_Typen()
    {
        Assert.True(IstErlaubterEreignisWert(typeof(Guid?), "Zeltlotse.Konten.Contracts.Events"));
        Assert.True(IstErlaubterEreignisWert(typeof(DateOnly), "Zeltlotse.Konten.Contracts.Events"));
        Assert.False(IstErlaubterEreignisWert(typeof(string), "Zeltlotse.Konten.Contracts.Events"));
        Assert.False(IstErlaubterEreignisWert(typeof(bool), "Zeltlotse.Konten.Contracts.Events"));
    }

    internal static bool IstErlaubterEreignisWert(Type typ, string eventsNamensraum)
    {
        var kern = Nullable.GetUnderlyingType(typ) ?? typ;
        return kern == typeof(Guid)
               || kern.IsEnum
               || kern == typeof(byte) || kern == typeof(sbyte) || kern == typeof(short) || kern == typeof(ushort)
               || kern == typeof(int) || kern == typeof(uint) || kern == typeof(long) || kern == typeof(ulong)
               || kern == typeof(float) || kern == typeof(double) || kern == typeof(decimal)
               || kern == typeof(DateTime) || kern == typeof(DateTimeOffset) || kern == typeof(DateOnly) || kern == typeof(TimeOnly)
               || kern.Namespace == eventsNamensraum;
    }

    private static bool IstCommand(Type typ) => typeof(IBaseCommand).IsAssignableFrom(typ) && !typ.IsInterface;

    private static bool IstCommandHandler(Type typ) =>
        !typ.IsInterface && typ.GetInterfaces().Any(i => i.IsGenericType &&
            (i.GetGenericTypeDefinition() == typeof(ICommandHandler<,>) || i.GetGenericTypeDefinition() == typeof(ICommandHandler<>)));

    private static bool IstValidator(Type typ) => typeof(IValidator).IsAssignableFrom(typ) && !typ.IsAbstract;
}
