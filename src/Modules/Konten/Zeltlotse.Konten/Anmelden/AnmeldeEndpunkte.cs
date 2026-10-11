using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Zeltlotse.Api.Contracts.Konten;
using Zeltlotse.Konten.Passwort;
using Zeltlotse.Konten.Sitzungen;
using Zeltlotse.SharedKernel.Mail;
using Zeltlotse.SharedKernel.Sicherheitsprotokoll;
using Zeltlotse.SharedKernel.Web;

namespace Zeltlotse.Konten.Anmelden;

/// <summary>
/// <c>POST /api/konten/login-link</c>: Login-Link und Login-Code per Mail, für bekannte und unbekannte Mail-Adressen gleich
/// (<c>202</c>). Die Mail geht direkt nach dem erfolgreichen Command raus, nicht über die Outbox (AD-13: selbst angefordert);
/// scheitert der Versand, bleibt die Anfrage bestehen und die Antwort ist <c>500</c>.
/// </summary>
internal static class LoginLinkAnfordernEndpunkt
{
    public static async Task<Accepted<LoginLinkAntwort>> Anfordern(
        LoginLinkAnfordern anfrage, ISender sender, IMailVersand mail, IConfiguration konfiguration, CancellationToken abbruch)
    {
        var neu = await sender.Send(new LoginAnfrageAnlegen(anfrage.Mail), abbruch);
        var adresse = OeffentlicheAdresse.Lesen(konfiguration);
        await mail.SendenAsync(neu.Mail, LoginMail.Betreff, LoginMail.Text(adresse, neu.Token, neu.Code), abbruch);
        return TypedResults.Accepted((string?)null, new LoginLinkAntwort(neu.AnfrageId));
    }
}

/// <summary><c>POST /api/konten/login-link/einloesen</c>: Token einlösen (nur per POST, AD-11); <c>204</c> mit Sitzung oder <c>404</c>.</summary>
internal static class LoginLinkEinloesenEndpunkt
{
    public static async Task<Results<NoContent, NotFound>> Einloesen(
        LoginLinkEinloesen anfrage, ISender sender, ISicherheitsprotokoll protokoll, HttpContext kontext, CancellationToken abbruch) =>
        await AnmeldungAbschliessen.Async(() => sender.Send(new LoginLinkEinloesenCommand(anfrage.Token), abbruch), protokoll, kontext, abbruch)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
}

/// <summary><c>POST /api/konten/login-code/einloesen</c>: Login-Code einlösen; <c>204</c> mit Sitzung oder <c>404</c>.</summary>
internal static class LoginCodeEinloesenEndpunkt
{
    public static async Task<Results<NoContent, NotFound>> Einloesen(
        LoginCodeEinloesen anfrage, ISender sender, ISicherheitsprotokoll protokoll, HttpContext kontext, CancellationToken abbruch) =>
        await AnmeldungAbschliessen.Async(() => sender.Send(new LoginCodeEinloesenCommand(anfrage.AnfrageId, anfrage.Code), abbruch), protokoll, kontext, abbruch)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
}

/// <summary>
/// <c>POST /api/konten/anmelden</c>: Mail-Adresse und Passwort; <c>204</c> mit Sitzung, sonst <c>401</c> – für falsches
/// Passwort, Konto ohne Passwort und unbekannte Mail gleich.
/// </summary>
internal static class PasswortAnmeldenEndpunkt
{
    public static async Task<Results<NoContent, UnauthorizedHttpResult>> Anmelden(
        PasswortAnmeldung anfrage, ISender sender, ISicherheitsprotokoll protokoll, HttpContext kontext, CancellationToken abbruch) =>
        await AnmeldungAbschliessen.Async(() => sender.Send(new PasswortAnmeldungPruefen(anfrage.Mail, anfrage.Passwort), abbruch), protokoll, kontext, abbruch)
            ? TypedResults.NoContent()
            : TypedResults.Unauthorized();
}

/// <summary><c>POST /api/konten/abmelden</c>: löscht das Sitzungs-Cookie.</summary>
internal static class AbmeldenEndpunkt
{
    public static async Task<NoContent> Abmelden(HttpContext kontext, CancellationToken abbruch)
    {
        abbruch.ThrowIfCancellationRequested();
        await SitzungAusstellen.AbmeldenAsync(kontext);
        return TypedResults.NoContent();
    }
}

/// <summary>Gemeinsamer Abschluss einer erfolgreichen Anmeldung: Sicherheitsprotokoll und Sitzungs-Cookie.</summary>
internal static class AnmeldungAbschliessen
{
    public static async Task<bool> Async(
        Func<ValueTask<SitzungsDaten?>> pruefen, ISicherheitsprotokoll protokoll, HttpContext kontext, CancellationToken abbruch)
    {
        SitzungsDaten? sitzung;
        try
        {
            sitzung = await pruefen();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Dieselbe Anfrage wurde gleichzeitig eingelöst: Nur eine Einlösung gilt.
            await protokoll.SchreibenAsync(SicherheitsEreignisArt.LinkEinloesungFehlgeschlagen, abbruch: abbruch);
            return false;
        }

        if (sitzung is null)
        {
            return false;
        }

        await protokoll.SchreibenAsync(SicherheitsEreignisArt.Anmeldung, sitzung.PersonId, abbruch: abbruch);
        await SitzungAusstellen.AnmeldenAsync(kontext, sitzung);
        return true;
    }
}
