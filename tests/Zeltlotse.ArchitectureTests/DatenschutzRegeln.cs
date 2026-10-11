using System.Reflection;
using ArchUnitNET.Domain;
using ArchUnitNET.Domain.Dependencies;
using ArchUnitNET.Fluent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Zeltlotse.SharedKernel.Datenschutz;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Zeltlotse.ArchitectureTests;

/// <summary>Regeln für Datenschutz und Protokollierung (AD-14, AD-22, AD-24).</summary>
internal static class DatenschutzRegeln
{
    private const string LoggerExtensions = "Microsoft.Extensions.Logging.LoggerExtensions";
    private const string OptionsBuilder = "Microsoft.EntityFrameworkCore.DbContextOptionsBuilder";

    /// <summary>
    /// AD-14: <c>[Besonders]</c> steht nur an Eigenschaften vom Typ <see cref="Geschuetzt"/>, und jede
    /// <see cref="Geschuetzt"/>-Eigenschaft trägt <c>[Besonders]</c>. Liefert die Verstöße als lesbare Sätze.
    /// </summary>
    public static IEnumerable<string> BesondersNurAlsGeschuetzt(IEnumerable<Type> typen)
    {
        const BindingFlags alle = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var typ in typen)
        {
            foreach (var eigenschaft in typ.GetProperties(alle))
            {
                var markiert = eigenschaft.IsDefined(typeof(BesondersAttribute), inherit: true);
                var geschuetzt = eigenschaft.PropertyType == typeof(Geschuetzt);
                if (markiert && !geschuetzt)
                {
                    yield return $"{typ.FullName}.{eigenschaft.Name} trägt [Besonders], ist aber {eigenschaft.PropertyType.Name} statt Geschuetzt.";
                }
                else if (geschuetzt && !markiert)
                {
                    yield return $"{typ.FullName}.{eigenschaft.Name} ist Geschuetzt, trägt aber kein [Besonders].";
                }
            }
        }
    }

    /// <summary>AD-22: Geloggt wird nur über <c>[LoggerMessage]</c>, nie über <c>LoggerExtensions.Log*</c>.</summary>
    public static IArchRule KeineLoggerExtensions(params string[] assemblies) =>
        Types().That().FollowCustomPredicate(t => AbhaengigkeitsRegeln.InAssembly(t, assemblies), "Typen der Lösung")
            .Should().FollowCustomCondition(
                t => !RuftAuf(t, LoggerExtensions, name => name.StartsWith("Log", StringComparison.Ordinal)),
                "nur über [LoggerMessage] loggen",
                "ruft LoggerExtensions.Log* auf");

    /// <summary>AD-22: Weder <c>EnableSensitiveDataLogging</c> noch <c>EnableDetailedErrors</c> im Quellcode.</summary>
    public static IArchRule KeineSensiblenEfProtokolle(params string[] assemblies) =>
        Types().That().FollowCustomPredicate(t => AbhaengigkeitsRegeln.InAssembly(t, assemblies), "Typen der Lösung")
            .Should().FollowCustomCondition(
                t => !RuftAuf(t, OptionsBuilder, name => name is "EnableSensitiveDataLogging" or "EnableDetailedErrors"),
                "weder EnableSensitiveDataLogging noch EnableDetailedErrors aufrufen",
                "ruft EnableSensitiveDataLogging oder EnableDetailedErrors auf");

    /// <summary>AD-3/AD-22: Tabellen, Sequenzen und Migrations-Historie eines DbContext außerhalb des erwarteten Schemas.</summary>
    public static IEnumerable<string> FremdeSchemas(DbContext db, string schema)
    {
        var fremd = db.Model.GetEntityTypes()
            .Where(e => e.GetTableName() is not null && e.GetSchema() != schema)
            .Select(e => $"{db.GetType().Name}: {e.Name} -> {e.GetSchema()}.{e.GetTableName()}")
            .Concat(db.Model.GetSequences().Where(s => s.Schema != schema).Select(s => $"{db.GetType().Name}: Sequenz {s.Name}"))
            .ToList();

        var relational = db.GetService<IDbContextOptions>().Extensions
            .OfType<Microsoft.EntityFrameworkCore.Infrastructure.RelationalOptionsExtension>()
            .SingleOrDefault();
        if (relational?.MigrationsHistoryTableSchema != schema)
        {
            fremd.Add($"{db.GetType().Name}: Migrations-Historie im Schema {relational?.MigrationsHistoryTableSchema ?? "(Standard)"}");
        }

        return fremd;
    }

    private static bool RuftAuf(IType typ, string deklarierenderTyp, Func<string, bool> methode) =>
        typ.Dependencies.OfType<MethodCallDependency>().Any(d =>
            d.TargetMember.DeclaringType.FullName.StartsWith(deklarierenderTyp, StringComparison.Ordinal)
            && methode(d.TargetMember.Name.Split('(')[0].Split('<')[0]));
}
