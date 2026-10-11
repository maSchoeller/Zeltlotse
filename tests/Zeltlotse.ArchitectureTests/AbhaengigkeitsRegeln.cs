using ArchUnitNET.Domain;
using ArchUnitNET.Domain.Extensions;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Zeltlotse.ArchitectureTests;

/// <summary>ArchUnitNET-Regeln auf den gebauten Assemblies (AD-2, AD-5, AD-24, AD-27, AD-28).</summary>
internal static class AbhaengigkeitsRegeln
{
    private const string SchreibKontextBasis = "Zeltlotse.SharedKernel.Daten.ModulDbContext";

    /// <summary>Die Architektur aller Lösungs-Assemblies (src und Testmodul), einmal geladen.</summary>
    public static Architecture Architektur { get; } = new ArchLoader()
        .LoadAssemblies([.. Loesung.Assemblies])
        .Build();

    /// <summary>Die Architektur der Negativ-Fixtures (dieses Testprojekt plus SharedKernel).</summary>
    public static Architecture Negativ { get; } = new ArchLoader()
        .LoadAssemblies(typeof(AbhaengigkeitsRegeln).Assembly, typeof(SharedKernel.Daten.ModulDbContext).Assembly)
        .Build();

    /// <summary>AD-27: Nur Command-Handler hängen vom schreibenden DbContext ab (Lösch-Dienst und Datenauskunft folgen mit ihren Stories).</summary>
    public static IArchRule NurHandlerNutzenSchreibKontext(Architecture architektur, params string[] assemblies) =>
        Types().That().FollowCustomPredicate(t => InAssembly(t, assemblies), "Typen der Module")
            .Should().FollowCustomCondition(
                t => !HaengtVonSchreibKontextAb(t) || DarfSchreibKontextNutzen(architektur, t),
                "nur als Command-Handler, DbContext, Migration oder Modul-Registrierung vom schreibenden DbContext abhängen",
                "hängt vom schreibenden DbContext ab, ist aber kein Command-Handler");

    /// <summary>AD-2: Lesende Contracts-Schnittstellen nur entlang der Pfeile; Ereignis-Typen darf jedes Modul nutzen.</summary>
    public static IArchRule ContractsNurEntlangDerPfeile(Modul modul) =>
        Types().That().FollowCustomPredicate(t => InAssembly(t, modul.Projekt), $"Typen von {modul.Projekt}")
            .Should().FollowCustomCondition(
                t => !t.GetTypeDependencies().Any(d => IstVerboteneContractsNutzung(modul, d)),
                "nur Contracts entlang der Pfeile aus AD-2 nutzen",
                "nutzt eine lesende Contracts-Schnittstelle gegen die Pfeile aus AD-2");

    /// <summary>AD-5: Ein Modul nutzt in Api.Contracts nur den eigenen Ordner und <c>Gemeinsam</c>.</summary>
    public static IArchRule ApiContractsNurEigenerOrdner(Modul modul) =>
        Types().That().FollowCustomPredicate(t => InAssembly(t, modul.Projekt), $"Typen von {modul.Projekt}")
            .Should().FollowCustomCondition(
                t => !t.GetTypeDependencies().Any(d => IstFremderApiContractsOrdner(modul, d)),
                "in Api.Contracts nur den eigenen Ordner und Gemeinsam nutzen",
                "nutzt Typen aus dem Api.Contracts-Ordner eines anderen Moduls");

    /// <summary>AD-27/AD-28: Handler rufen weder <c>IMediator.Publish</c> noch die Outbox direkt auf.</summary>
    public static IArchRule HandlerOhnePublishUndOutbox(params string[] assemblies) =>
        Types().That().FollowCustomPredicate(t => InAssembly(t, assemblies) && IstCommandHandler(t), "Command-Handler")
            .Should().FollowCustomCondition(
                t => !t.GetTypeDependencies().Any(IstPublishOderOutbox),
                "weder IMediator/IPublisher noch die Outbox nutzen",
                "nutzt IMediator/IPublisher oder die Outbox direkt");

    public static bool InAssembly(IType typ, params string[] assemblies) =>
        assemblies.Contains(KurzName(typ.Assembly));

    private static string KurzName(Assembly assembly) => assembly.Name.Split(',')[0];

    private static bool HaengtVonSchreibKontextAb(IType typ) =>
        typ.GetTypeDependencies().Any(IstSchreibKontext);

    private static bool IstSchreibKontext(IType typ) =>
        typ is Class klasse && klasse.FullName != SchreibKontextBasis && ErbtVon(klasse, SchreibKontextBasis);

    private static bool ErbtVon(Class klasse, string basis)
    {
        for (var aktuell = klasse.BaseClass; aktuell is not null; aktuell = aktuell.BaseClass)
        {
            if (aktuell.FullName == basis)
            {
                return true;
            }
        }

        return false;
    }

    private static bool DarfSchreibKontextNutzen(Architecture architektur, IType typ)
    {
        var aeusserer = AeussererTyp(architektur, typ);
        return IstSchreibKontext(aeusserer)
               || IstCommandHandler(aeusserer)
               || aeusserer.Namespace.FullName.EndsWith(".Migrationen", StringComparison.Ordinal)
               || aeusserer.ImplementedInterfaces.Any(i => i.FullName.StartsWith("Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory", StringComparison.Ordinal))
               || IstReineModulRegistrierung(typ);
    }

    /// <summary>Vom Compiler erzeugte Hilfstypen (Lambdas, async-Zustandsmaschinen) zählen zum umgebenden Typ.</summary>
    private static IType AeussererTyp(Architecture architektur, IType typ)
    {
        var trenner = typ.FullName.IndexOfAny(['+', '/']);
        if (trenner < 0)
        {
            return typ;
        }

        var name = typ.FullName[..trenner];
        return architektur.Types.FirstOrDefault(t => t.FullName == name) ?? typ;
    }

    private static bool IstCommandHandler(IType typ) =>
        typ.ImplementedInterfaces.Any(i => i.FullName.StartsWith("Mediator.ICommandHandler", StringComparison.Ordinal));

    /// <summary>
    /// Die Registrierung darf den DbContext nur in ihren <c>Add…</c>-Methoden nennen (AddDbContext). ArchUnit rechnet
    /// Lambdas der umgebenden Methode zu; ein Endpunkt-Lambda in <c>Map…Endpoints</c> fällt so auf.
    /// </summary>
    private static bool IstReineModulRegistrierung(IType typ) =>
        IstModulRegistrierung(typ)
        && typ.Members
            .Where(m => m.GetTypeDependencies().Any(IstSchreibKontext))
            .All(m => m.Name.StartsWith("Add", StringComparison.Ordinal));

    private static bool IstModulRegistrierung(IType typ) =>
        typ.Name.EndsWith("Modul", StringComparison.Ordinal) && typ.Namespace.FullName == KurzName(typ.Assembly);

    private static bool IstVerboteneContractsNutzung(Modul modul, IType ziel)
    {
        var ns = ziel.Namespace.FullName;
        foreach (var anderes in Loesung.Module.Where(m => m != modul))
        {
            var istContracts = ns == anderes.ContractsProjekt || ns.StartsWith(anderes.ContractsProjekt + ".", StringComparison.Ordinal);
            var istEvents = ns == anderes.EventsNamensraum || ns.StartsWith(anderes.EventsNamensraum + ".", StringComparison.Ordinal);
            if (istContracts && !istEvents && !Loesung.Pfeile.Contains((modul.Name, anderes.Name)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IstFremderApiContractsOrdner(Modul modul, IType ziel)
    {
        const string wurzel = "Zeltlotse.Api.Contracts.";
        var ns = ziel.Namespace.FullName + ".";
        if (!ns.StartsWith(wurzel, StringComparison.Ordinal))
        {
            return false;
        }

        return !ns.StartsWith($"{wurzel}{modul.Name}.", StringComparison.Ordinal)
               && !ns.StartsWith($"{wurzel}Gemeinsam.", StringComparison.Ordinal);
    }

    private static bool IstPublishOderOutbox(IType ziel) =>
        ziel.FullName is "Mediator.IMediator" or "Mediator.IPublisher"
        || ziel.Namespace.FullName.StartsWith("Zeltlotse.SharedKernel.Outbox", StringComparison.Ordinal);
}
