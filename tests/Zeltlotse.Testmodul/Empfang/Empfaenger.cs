using Mediator;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.Testmodul.Daten;
using Zeltlotse.Testmodul.Events;

namespace Zeltlotse.Testmodul.Empfang;

internal sealed record AngelegtEmpfangenA(Umschlag Umschlag, Guid NotizId)
    : ICommand, IModulCommand<TestmodulDbContext>, IEreignisEmpfaenger<AngelegtEmpfangenA, NotizAngelegt>
{
    public static AngelegtEmpfangenA Aus(Umschlag umschlag, NotizAngelegt ereignis) => new(umschlag, ereignis.NotizId);
}

internal sealed record AngelegtEmpfangenB(Umschlag Umschlag, Guid NotizId)
    : ICommand, IModulCommand<TestmodulDbContext>, IEreignisEmpfaenger<AngelegtEmpfangenB, NotizAngelegt>
{
    public static AngelegtEmpfangenB Aus(Umschlag umschlag, NotizAngelegt ereignis) => new(umschlag, ereignis.NotizId);
}

internal sealed record GeaendertEmpfangen(Umschlag Umschlag, Guid NotizId)
    : ICommand, IModulCommand<TestmodulDbContext>, IEreignisEmpfaenger<GeaendertEmpfangen, NotizGeaendert>
{
    public static GeaendertEmpfangen Aus(Umschlag umschlag, NotizGeaendert ereignis) => new(umschlag, ereignis.NotizId);
}

internal sealed class AngelegtEmpfangenAHandler(TestmodulDbContext db, EmpfangsStoerung stoerung, IMandantenKontext kontext, TimeProvider uhr)
    : ICommandHandler<AngelegtEmpfangenA>
{
    public ValueTask<Unit> Handle(AngelegtEmpfangenA command, CancellationToken cancellationToken)
    {
        stoerung.PruefenUndAusloesen(nameof(AngelegtEmpfangenA), command.Umschlag.Folgenummer);
        db.Empfaenge.Add(Empfang.Neu(nameof(AngelegtEmpfangenA), command.Umschlag, command.NotizId, kontext.OrganisationId, uhr));
        return ValueTask.FromResult(Unit.Value);
    }
}

internal sealed class AngelegtEmpfangenBHandler(TestmodulDbContext db, EmpfangsStoerung stoerung, IMandantenKontext kontext, TimeProvider uhr)
    : ICommandHandler<AngelegtEmpfangenB>
{
    public ValueTask<Unit> Handle(AngelegtEmpfangenB command, CancellationToken cancellationToken)
    {
        stoerung.PruefenUndAusloesen(nameof(AngelegtEmpfangenB), command.Umschlag.Folgenummer);
        db.Empfaenge.Add(Empfang.Neu(nameof(AngelegtEmpfangenB), command.Umschlag, command.NotizId, kontext.OrganisationId, uhr));
        return ValueTask.FromResult(Unit.Value);
    }
}

internal sealed class GeaendertEmpfangenHandler(TestmodulDbContext db, EmpfangsStoerung stoerung, IMandantenKontext kontext, TimeProvider uhr)
    : ICommandHandler<GeaendertEmpfangen>
{
    public ValueTask<Unit> Handle(GeaendertEmpfangen command, CancellationToken cancellationToken)
    {
        stoerung.PruefenUndAusloesen(nameof(GeaendertEmpfangen), command.Umschlag.Folgenummer);
        db.Empfaenge.Add(Empfang.Neu(nameof(GeaendertEmpfangen), command.Umschlag, command.NotizId, kontext.OrganisationId, uhr));
        return ValueTask.FromResult(Unit.Value);
    }
}
