using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Zeltlotse.SharedKernel.Anmeldung;

namespace Zeltlotse.Konten.Sitzungen;

/// <summary>
/// Stellt das Sitzungs-Cookie aus bzw. löscht es (AD-12, AD-25); gleitend 30 Tage, auch über Neustarts des Browsers. Jede
/// Anmeldung setzt den Zeitpunkt der Anmeldung auf „jetzt“; das Neu-Ausstellen derselben Anmeldung (nach „Passwort
/// festlegen“) übernimmt den bisherigen Zeitpunkt, damit es keine erneute Anmeldung ersetzt.
/// </summary>
internal static class SitzungAusstellen
{
    public static Task AnmeldenAsync(HttpContext kontext, SitzungsDaten sitzung) =>
        AusstellenAsync(kontext, sitzung, kontext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow());

    public static Task NeuAusstellenAsync(HttpContext kontext, SitzungsDaten sitzung) =>
        AusstellenAsync(kontext, sitzung, Sitzung.AngemeldetAm(kontext.User));

    public static Task AbmeldenAsync(HttpContext kontext) => kontext.SignOutAsync(Sitzung.Schema);

    private static Task AusstellenAsync(HttpContext kontext, SitzungsDaten sitzung, DateTimeOffset? angemeldetAm) =>
        kontext.SignInAsync(
            Sitzung.Schema,
            Sitzung.Prinzipal(sitzung.KontoId, sitzung.PersonId, sitzung.SitzungsStempel, angemeldetAm),
            new AuthenticationProperties { IsPersistent = true });
}
