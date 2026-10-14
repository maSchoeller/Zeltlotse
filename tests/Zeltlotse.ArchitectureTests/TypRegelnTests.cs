using System.Reflection;
using FluentValidation;
using Mediator;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mail;

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

    /// <summary>
    /// Story 3-8 (AD-17): Jeder Benutzer-Command mit einer Eigenschaft <c>Guid FreizeitId</c> oder <c>Guid? FreizeitId</c> trägt <see cref="IFreizeitCommand"/>, damit die
    /// Schreibschutz-Stufe ihn nach dem Abschluss ablehnt. So entstehen bei künftigen Commands keine Lücken.
    /// </summary>
    [Fact]
    public void Jeder_Benutzer_Command_mit_FreizeitId_traegt_IFreizeitCommand()
    {
        var verstoesse = Loesung.ModulAssemblies
            .SelectMany(AlleTypen)
            .Where(t => IstCommand(t) && typeof(IBenutzerCommand).IsAssignableFrom(t))
            .Where(t => t.GetProperty("FreizeitId", BindingFlags.Public | BindingFlags.Instance)?.PropertyType is { } typ && (typ == typeof(Guid) || typ == typeof(Guid?)))
            .Where(t => !typeof(IFreizeitCommand).IsAssignableFrom(t))
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(verstoesse);
    }

    /// <summary>Story 3-8 (AD-17): <see cref="ErlaubtNachAbschlussAttribute"/> steht nur an den Anschaffungswünschen in Inventar (Finanzen folgt).</summary>
    [Fact]
    public void ErlaubtNachAbschluss_steht_nur_an_Schaeden_und_Anschaffungswuenschen()
    {
        var erlaubt = Loesung.ModulAssemblies
            .SelectMany(AlleTypen)
            .Where(t => t.IsDefined(typeof(ErlaubtNachAbschlussAttribute), inherit: false))
            .Select(t => t.FullName!)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            [
                "Zeltlotse.Inventar.Schaeden.AnschaffungswunschErledigtCommand",
                "Zeltlotse.Inventar.Schaeden.SchadenEintragenCommand",
                "Zeltlotse.Inventar.Schaeden.SchadenEntfernenCommand",
                "Zeltlotse.Inventar.Schaeden.SchadenFeldAendernCommand",
            ],
            erlaubt);
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
    public void Mail_Auftraege_bestehen_nur_aus_Guid_Zahlen_Datumswerten_und_Enums()
    {
        // AD-13, AD-4, AD-22: Adresse, Namen und Text entstehen erst beim Versand; in der Outbox stehen nur Kennungen.
        var auftraege = ModulUndTestmodul
            .SelectMany(AlleTypen)
            .Where(t => typeof(IMailAuftrag).IsAssignableFrom(t) && !t.IsInterface)
            .ToList();
        Assert.Contains(auftraege, t => t.Assembly.GetName().Name == "Zeltlotse.Testmodul");
        Assert.Contains(auftraege, t => t.FullName == "Zeltlotse.Personen.Nachweise.NachweisErinnerungMail");

        var verstoesse = new List<string>();
        foreach (var typ in auftraege)
        {
            var mitglieder = typ.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Select(p => (p.Name, Typ: p.PropertyType))
                .Concat(typ.GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => (f.Name, Typ: f.FieldType)))
                .Where(m => m.Name != "EqualityContract");
            foreach (var (name, mitgliedTyp) in mitglieder)
            {
                // Kein Namensraum ist erlaubt: Unter-Typen gibt es in Mail-Aufträgen nicht.
                if (!IstErlaubterEreignisWert(mitgliedTyp, "\0"))
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
