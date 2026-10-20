using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.SharedKernel.Mail;
using Zeltlotse.Testmodul.Daten;
using Zeltlotse.Testmodul.Empfang;
using Zeltlotse.Testmodul.Events;
using Zeltlotse.Testmodul.Mails;
using Zeltlotse.Testmodul.Pushes;
using Zeltlotse.SharedKernel.Push;

namespace Zeltlotse.Testmodul;

/// <summary>Registrierung des Testmoduls, aufgebaut wie die eines Fachmoduls.</summary>
public static class TestmodulModul
{
    internal const string Schema = "testmodul";

    public static IServiceCollection AddTestmodul(this IServiceCollection services)
    {
        services.AddModul<TestmodulDbContext, TestmodulLeseDbContext>("Testmodul", Schema);
        services.AddSingleton<EmpfangsStoerung>();
        services.EmpfaengtEreignis<NotizAngelegt, AngelegtEmpfangenA>();
        services.EmpfaengtEreignis<NotizAngelegt, AngelegtEmpfangenB>();
        services.EmpfaengtEreignis<NotizGeaendert, GeaendertEmpfangen>();
        services.AddSingleton<MailAufbauStoerung>();
        services.VerschicktMail<NotizMail, NotizMailAufbau>();
        services.AddSingleton<PushAufbauStoerung>();
        services.VerschicktPush<NotizPush, NotizPushAufbau>();
        return services;
    }
}
