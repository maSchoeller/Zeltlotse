using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.SharedKernel.Commands;

namespace Zeltlotse.SharedKernel.Ereignisse;

/// <summary>Ereignis-Empfänger: ein eigener Command im Zielmodul mit dem Umschlag als Eigenschaft (AD-4).</summary>
public interface IEreignisEmpfaenger
{
    Umschlag Umschlag { get; }
}

/// <summary>Empfänger für genau ein Ereignis; die Zustellung erzeugt den Command mit <see cref="Aus"/>.</summary>
public interface IEreignisEmpfaenger<TSelf, TEreignis> : IEreignisEmpfaenger
    where TSelf : IEreignisEmpfaenger<TSelf, TEreignis>
    where TEreignis : class
{
    static abstract TSelf Aus(Umschlag umschlag, TEreignis ereignis);
}

/// <summary>Ein in der Modul-Registrierung angemeldeter Empfänger.</summary>
public sealed record EmpfaengerEintrag(
    Type EreignisTyp,
    Type CommandTyp,
    Func<Umschlag, object, ICommand> Erzeugen)
{
    public string EreignisName => EreignisTyp.FullName!;

    public string Empfaenger => CommandTyp.FullName!;
}

public static class EmpfaengerRegistrierung
{
    /// <summary>Meldet einen Empfänger für ein Ereignis an (AD-4). Mediator-Notifications werden dafür nicht verwendet.</summary>
    public static IServiceCollection EmpfaengtEreignis<TEreignis, TCommand>(this IServiceCollection services)
        where TEreignis : class
        where TCommand : class, ICommand, IModulCommandBasis, IEreignisEmpfaenger<TCommand, TEreignis>
    {
        services.AddSingleton(new EmpfaengerEintrag(
            typeof(TEreignis),
            typeof(TCommand),
            (umschlag, ereignis) => TCommand.Aus(umschlag, (TEreignis)ereignis)));
        return services;
    }
}
