using Microsoft.Extensions.DependencyInjection;

namespace Zeltlotse.SharedKernel.Push;

/// <summary>
/// Story 6-1: Ein Push-Auftrag (AD-13, AD-21), gespiegelt vom Mail-Auftrag: Ein Aggregat merkt ihn mit <c>PushVormerken</c> vor, die Outbox-Stufe
/// schreibt ihn im selben <c>SaveChanges</c> in die Outbox, und die Zustellung baut die Sendung erst beim Versand mit dem registrierten
/// <see cref="IPushAufbau{TAuftrag}"/>. Ein Push-Auftrag besteht nur aus <see cref="Guid"/>, Zahlen, Datumswerten und Enums; Text, Ziele und Schlüssel
/// stehen nie in der Outbox (ein Architekturtest prüft das).
/// </summary>
public interface IPushAuftrag;

/// <summary>
/// Baut beim Versand die Sendung zu einem Auftrag, im eigenen DI-Scope mit dem Mandanten-Kontext aus dem Umschlag. <c>null</c> heißt: nichts zu
/// senden, die Zustellung ist erledigt. Eine Ausnahme beim Aufbau wird mit Wartezeit wiederholt (es ist dann noch nichts gesendet).
/// </summary>
public interface IPushAufbau<in TAuftrag>
    where TAuftrag : IPushAuftrag
{
    Task<PushSendung?> AufbauenAsync(TAuftrag auftrag, CancellationToken abbruch);

    /// <summary>Die Push-Dienste melden diese Ziele als abgelaufen (404/410): Der Besitzer der Abos entfernt sie (über einen eigenen Command).</summary>
    Task UngueltigeZieleAsync(IReadOnlyCollection<Guid> zielIds, CancellationToken abbruch);
}

/// <summary>Was ein Push zeigt: Titel, Text und die Seite, die ein Tipp öffnet. Entsteht erst beim Versand und erscheint nie in Logs (AD-22).</summary>
public sealed record PushInhalt(string Titel, string Text, string Url);

/// <summary>Ein Push-Abo als Ziel: Kennung beim Besitzer, Adresse beim Push-Dienst und die Schlüssel des Browsers (Web-Push-Standard).</summary>
public sealed record PushZiel(Guid Id, string Endpunkt, string P256dh, string Auth)
{
    /// <summary>Nie Adresse oder Schlüssel ausgeben.</summary>
    public override string ToString() => $"{nameof(PushZiel)}({Id})";
}

/// <summary>Ein Inhalt an mehrere Ziele.</summary>
public sealed record PushSendung(PushInhalt Inhalt, IReadOnlyList<PushZiel> Ziele);

/// <summary>Antwort des Push-Dienstes auf einen Versuch.</summary>
public enum PushErgebnis
{
    Zugestellt = 1,

    /// <summary>Der Push-Dienst kennt das Abo nicht mehr (404 oder 410): Es ist abzuräumen.</summary>
    ZielUngueltig = 2,
}

/// <summary>
/// Push-Versand nach dem Web-Push-Standard (AD-21, AD-23). Der Adapter steht im API-Host; nur dort gibt es Code für das Protokoll. Andere Fehler als
/// „Ziel ungültig“ enden in einer Ausnahme.
/// </summary>
public interface IPushVersand
{
    Task<PushErgebnis> SendenAsync(PushZiel ziel, PushInhalt inhalt, CancellationToken abbruch = default);
}

/// <summary>Ein angemeldeter Push-Auftrag mit den Fabriken, die seinen Aufbau im Scope der Zustellung aufrufen.</summary>
public sealed record PushAuftragEintrag(
    Type AuftragTyp,
    Func<IServiceProvider, object, CancellationToken, Task<PushSendung?>> Aufbauen,
    Func<IServiceProvider, IReadOnlyCollection<Guid>, CancellationToken, Task> UngueltigeZiele)
{
    /// <summary>Name des Typs, wie er in der Outbox steht.</summary>
    public string Name => AuftragTyp.FullName!;
}

/// <summary>Alle angemeldeten Push-Aufträge, nach dem Typnamen in der Outbox nachschlagbar.</summary>
public sealed class PushKatalog(IEnumerable<PushAuftragEintrag> eintraege)
{
    private readonly IReadOnlyList<PushAuftragEintrag> _eintraege = [.. eintraege];

    public IReadOnlyList<PushAuftragEintrag> Alle => _eintraege;

    public PushAuftragEintrag? Finden(string name) => _eintraege.FirstOrDefault(e => e.Name == name);
}

public static class PushAuftraege
{
    /// <summary>
    /// Fester Empfänger-Name der einen Zustellung je Push-Auftrag. Kein Ereignis-Empfänger: keine Inbox, keine Zeile im Ereignis-Fluss.
    /// </summary>
    public const string Empfaenger = "Zeltlotse.Push";

    /// <summary>Meldet den Aufbau der Pushes eines Auftrags-Typs an (in der Modul-Registrierung).</summary>
    public static IServiceCollection VerschicktPush<TAuftrag, TAufbau>(this IServiceCollection services)
        where TAuftrag : class, IPushAuftrag
        where TAufbau : class, IPushAufbau<TAuftrag>
    {
        services.AddScoped<IPushAufbau<TAuftrag>, TAufbau>();
        services.AddSingleton(new PushAuftragEintrag(
            typeof(TAuftrag),
            (dienste, auftrag, abbruch) => dienste.GetRequiredService<IPushAufbau<TAuftrag>>().AufbauenAsync((TAuftrag)auftrag, abbruch),
            (dienste, ziele, abbruch) => dienste.GetRequiredService<IPushAufbau<TAuftrag>>().UngueltigeZieleAsync(ziele, abbruch)));
        return services;
    }
}
