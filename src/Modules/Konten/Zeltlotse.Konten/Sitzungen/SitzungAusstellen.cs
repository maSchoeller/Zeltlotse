using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Zeltlotse.SharedKernel.Anmeldung;

namespace Zeltlotse.Konten.Sitzungen;

/// <summary>Stellt das Sitzungs-Cookie aus bzw. löscht es (AD-12, AD-25); gleitend 30 Tage, auch über Neustarts des Browsers.</summary>
internal static class SitzungAusstellen
{
    public static Task AnmeldenAsync(HttpContext kontext, SitzungsDaten sitzung) =>
        kontext.SignInAsync(
            Sitzung.Schema,
            Sitzung.Prinzipal(sitzung.KontoId, sitzung.PersonId, sitzung.SitzungsStempel),
            new AuthenticationProperties { IsPersistent = true });

    public static Task AbmeldenAsync(HttpContext kontext) => kontext.SignOutAsync(Sitzung.Schema);
}
