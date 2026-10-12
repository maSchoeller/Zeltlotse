using Microsoft.Extensions.DependencyInjection;

namespace Zeltlotse.SharedKernel.Mail;

/// <summary>
/// Ein Mail-Auftrag (AD-13, AD-4): Ein Aggregat merkt ihn mit <c>MailVormerken</c> vor, die Outbox-Stufe schreibt ihn im selben
/// <c>SaveChanges</c> in die Outbox, und die Zustellung baut die Mail erst beim Versand mit dem registrierten
/// <see cref="IMailAufbau{TAuftrag}"/>. Ein Mail-Auftrag besteht nur aus <see cref="Guid"/>, Zahlen, Datumswerten und Enums; Adresse,
/// Namen und Text stehen nie in der Outbox (ein Architekturtest prüft das).
/// </summary>
public interface IMailAuftrag;

/// <summary>
/// Baut beim Versand die Mail zu einem Auftrag, im eigenen DI-Scope mit dem Mandanten-Kontext aus dem Umschlag. <c>null</c> heißt:
/// nichts zu senden (z. B. kein Konto), die Zustellung ist erledigt. Eine Ausnahme wird mit Wartezeit wiederholt.
/// </summary>
public interface IMailAufbau<in TAuftrag>
    where TAuftrag : IMailAuftrag
{
    Task<MailInhalt?> AufbauenAsync(TAuftrag auftrag, CancellationToken abbruch);
}

/// <summary>Die fertige Mail; entsteht erst beim Versand und erscheint nie in Logs (AD-22).</summary>
public sealed record MailInhalt(string An, string Betreff, string Text);

/// <summary>Ein angemeldeter Mail-Auftrag mit der Fabrik, die seinen Aufbau im Scope der Zustellung aufruft.</summary>
public sealed record MailAuftragEintrag(Type AuftragTyp, Func<IServiceProvider, object, CancellationToken, Task<MailInhalt?>> Aufbauen)
{
    /// <summary>Name des Typs, wie er in der Outbox steht.</summary>
    public string Name => AuftragTyp.FullName!;
}

/// <summary>Alle angemeldeten Mail-Aufträge, nach dem Typnamen in der Outbox nachschlagbar.</summary>
public sealed class MailKatalog(IEnumerable<MailAuftragEintrag> eintraege)
{
    private readonly IReadOnlyList<MailAuftragEintrag> _eintraege = [.. eintraege];

    public IReadOnlyList<MailAuftragEintrag> Alle => _eintraege;

    public MailAuftragEintrag? Finden(string name) => _eintraege.FirstOrDefault(e => e.Name == name);
}

public static class MailAuftraege
{
    /// <summary>
    /// Fester Empfänger-Name der einen Zustellung je Mail-Auftrag. Kein Ereignis-Empfänger: keine Inbox, keine Zeile im
    /// Ereignis-Fluss.
    /// </summary>
    public const string Empfaenger = "Zeltlotse.Mail";

    /// <summary>Meldet den Aufbau der Mails eines Auftrags-Typs an (in der Modul-Registrierung).</summary>
    public static IServiceCollection VerschicktMail<TAuftrag, TAufbau>(this IServiceCollection services)
        where TAuftrag : class, IMailAuftrag
        where TAufbau : class, IMailAufbau<TAuftrag>
    {
        services.AddScoped<IMailAufbau<TAuftrag>, TAufbau>();
        services.AddSingleton(new MailAuftragEintrag(
            typeof(TAuftrag),
            (dienste, auftrag, abbruch) => dienste.GetRequiredService<IMailAufbau<TAuftrag>>().AufbauenAsync((TAuftrag)auftrag, abbruch)));
        return services;
    }
}
