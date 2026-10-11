using Microsoft.EntityFrameworkCore;
using Zeltlotse.Organisationen.Domaene;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Zugangslinks;
using OrgRolleArt = Zeltlotse.SharedKernel.Rechte.OrgRolle;

namespace Zeltlotse.Organisationen.Einladungen;

/// <summary>
/// Eine eben angelegte Einladung. Das Token im Klartext gibt es nur hier, für die Einladungs-Mail; gespeichert ist nur
/// der Hash (AD-11).
/// </summary>
internal sealed record NeueEinladung(Guid EinladungId, Guid OrganisationId, string OrganisationName, string Mail, OrgRolleArt Rolle, string Token)
{
    /// <summary>Nie Mail-Adresse oder Token ausgeben (AD-22).</summary>
    public override string ToString() => $"NeueEinladung({EinladungId})";
}

/// <summary>
/// Gemeinsamer Schritt von „Organisation anlegen“ und „Einladung anlegen“: verwirft ältere offene Einladungen derselben
/// Mail-Adresse für dieselbe Organisation und Rolle und legt die neue an. Bekommt nur die Tabelle des Handlers, nicht
/// den schreibenden DbContext (AD-27); die Tabelle läuft unter dem Mandantenfilter der Organisation.
/// </summary>
internal static class EinladungAusstellen
{
    public static async Task<NeueEinladung> Async(
        DbSet<OrgEinladung> einladungen, Organisation organisation, string mail, OrgRolleArt rolle, Guid eingeladenVon, TimeProvider uhr, CancellationToken abbruch)
    {
        var normalisiert = MailAdresse.Normalisieren(mail);
        var offene = await einladungen
            .Where(e => e.OrganisationId == organisation.Id && e.Mail == normalisiert && e.Rolle == rolle && e.Status == OrgEinladungStatus.Offen)
            .ToListAsync(abbruch);
        foreach (var aeltere in offene)
        {
            aeltere.Verwerfen();
        }

        var token = Zugangslink.NeuesToken();
        var einladung = OrgEinladung.Anlegen(
            Kennung.Neu(uhr), organisation.Id, normalisiert, rolle, Zugangslink.TokenHash(OrgEinladung.Zweck, token), eingeladenVon, uhr.GetUtcNow());
        einladungen.Add(einladung);
        return new NeueEinladung(einladung.Id, organisation.Id, organisation.Name, normalisiert, rolle, token);
    }
}
