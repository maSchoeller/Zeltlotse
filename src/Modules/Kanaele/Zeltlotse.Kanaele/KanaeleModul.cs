using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.Freizeiten.Contracts.Events;
using Zeltlotse.Kanaele.Daten;
using Zeltlotse.Kanaele.Datenschutz;
using Zeltlotse.Kanaele.Nachrichten;
using Zeltlotse.Kanaele.Notizen;
using Zeltlotse.Kanaele.Push;
using Zeltlotse.Personen.Contracts.Events;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Ereignisse;
using Zeltlotse.SharedKernel.Push;
using Zeltlotse.SharedKernel.Rechte;
using FreizeitRolle = Zeltlotse.SharedKernel.Rechte.FreizeitRolle;

namespace Zeltlotse.Kanaele;

/// <summary>Einzige öffentliche Oberfläche des Moduls: Registrierung und Endpunkte (AD-27).</summary>
public static class KanaeleModul
{
    internal const string Name = "Kanaele";

    internal const string Schema = "kanaele";

    /// <summary>Story 6-1: Leitung und Team lesen und schreiben in beiden Kanälen. Es gibt keinen Funktions-Schalter (AD-20: der Team-Kanal ist nie abschaltbar).</summary>
    internal static readonly FreizeitRolle[] Mitarbeit = [FreizeitRolle.Leitung, FreizeitRolle.Team];

    public static IServiceCollection AddKanaeleModul(this IServiceCollection services)
    {
        services.AddModul<KanaeleDbContext, KanaeleLeseDbContext>(Name, Schema);
        // Story 6-1: Jede gesendete Nachricht geht als Web-Push über die Outbox an die Abos von Leitung und Team (AD-13, AD-21). Nachrichten sind
        // Arbeitsdaten der Freizeit, Push-Abos personeneigen (AD-16): Empfänger von PersonGeloescht, Datenauskunft und Lösch-Lauf.
        services.VerschicktPush<NachrichtPush, NachrichtPushAufbau>();
        services.EmpfaengtEreignis<PersonGeloescht, KanaelePersonLoeschen>();
        // Story 6-4: Eine Nachricht in „Alle“ geht zusätzlich an die Gast-Abos der QR-Gäste; der Abschluss der Freizeit beendet Gast- und Team-Abos (AD-21).
        services.VerschicktPush<NachrichtGastPush, NachrichtGastPushAufbau>();
        services.EmpfaengtEreignis<FreizeitAbgeschlossen, GastAbosBeenden>();
        services.AddScoped<IDatenauskunft, KanaeleAuskunft>();
        services.AddScoped<ILoeschlauf, KanaeleLoeschlauf>();
        return services;
    }

    /// <summary>
    /// Bindet die Endpunkte des Moduls in die <c>/api</c>-Gruppe ein. Story 6-1: die zwei festen Kanäle unter
    /// <c>/api/freizeiten/{freizeitId}/kanaele/{team|alle}/nachrichten</c> (lesen, senden; „Alle“ nur mit Bestätigung) und das Push-Abo unter
    /// <c>…/kanaele/push</c> (öffentlicher Schlüssel, Abo speichern, abmelden), alle für Leitung und Team, ohne Funktions-Schalter; nach dem Abschluss nur
    /// lesbar (AD-17). Story 6-3: Ansagen-Zettel und Besprechungspunkte unter <c>…/kanaele/notizen</c> (lesen, anlegen, abhaken, wieder öffnen). Story 6-4:
    /// für den QR-Gast unter <c>/api/teilnehmer</c> der Kanal „Alle“ (nur lesen) und das eigene Push-Abo (<c>.NurQrGast()</c>).
    /// </summary>
    public static IEndpointRouteBuilder MapKanaeleEndpoints(this IEndpointRouteBuilder api)
    {
        var kanaele = api.MapGroup($"/freizeiten/{{{Rechtemerkmale.FreizeitRouteWert}:guid}}/kanaele");
        kanaele.MapGet("/push/schluessel", PushAboEndpunkte.Schluessel).NurFreizeitRolle(Mitarbeit);
        kanaele.MapPost("/push/abo", PushAboEndpunkte.Speichern).NurFreizeitRolle(Mitarbeit);
        kanaele.MapPost("/push/abo/abmelden", PushAboEndpunkte.Abmelden).NurFreizeitRolle(Mitarbeit);
        kanaele.MapGet("/{kanal}/nachrichten", NachrichtenLadenEndpunkt.Laden).NurFreizeitRolle(Mitarbeit);
        kanaele.MapPost("/{kanal}/nachrichten", NachrichtSendenEndpunkt.Senden).NurFreizeitRolle(Mitarbeit);
        // Story 6-3: Ansagen-Zettel und Besprechungspunkte. Das Literal „notizen“ hat Vorrang vor dem Kanal-Parameter der Nachrichten.
        kanaele.MapGet("/notizen/{art}", NotizenLadenEndpunkt.Laden).NurFreizeitRolle(Mitarbeit);
        kanaele.MapPost("/notizen/{art}", NotizAnlegenEndpunkt.Anlegen).NurFreizeitRolle(Mitarbeit);
        kanaele.MapPost("/notizen/{notizId:guid}/erledigt", NotizUebergangEndpunkt.Erledigt).NurFreizeitRolle(Mitarbeit);
        kanaele.MapPost("/notizen/{notizId:guid}/offen", NotizUebergangEndpunkt.Offen).NurFreizeitRolle(Mitarbeit);

        // Story 6-4: Der QR-Gast liest nur den Kanal „Alle“; schreiben darf er nur das eigene Push-Abo (gleiche Prüfung wie beim Team).
        var teilnehmer = api.MapGroup("/teilnehmer");
        teilnehmer.MapGet("/kanal", TeilnehmerKanalLadenEndpunkt.Laden).NurQrGast();
        teilnehmer.MapGet("/push/schluessel", GastPushEndpunkte.Schluessel).NurQrGast();
        teilnehmer.MapPost("/push/abo", GastPushEndpunkte.Speichern).NurQrGast();
        teilnehmer.MapPost("/push/abo/abmelden", GastPushEndpunkte.Abmelden).NurQrGast();
        return api;
    }
}
