using Microsoft.EntityFrameworkCore;
using Zeltlotse.Freizeiten.Daten;
using Zeltlotse.Freizeiten.Domaene;
using Zeltlotse.Personen.Contracts.Anmeldung;
using Zeltlotse.Personen.Contracts.Namen;
using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Mail;

namespace Zeltlotse.Freizeiten.Anmeldungen.Entscheiden;

/// <summary>Was die Leitung entschieden hat; steht im Mail-Auftrag. Werte sind gespeichert; nur anhängen.</summary>
internal enum Entscheidung
{
    Bestaetigt = 1,
    Abgesagt = 2,
}

/// <summary>
/// Mail-Auftrag „Anmeldung bestätigt“ bzw. „Anmeldung abgesagt“ (FR-20, FR-21, AD-13): nur Kennung und Entscheidung. Empfänger,
/// Namen und Text entstehen erst beim Versand (<see cref="AnmeldungEntschiedenMailAufbau"/>); nichts davon steht in der Outbox.
/// Der Auftrag entsteht nur durch den Knopfdruck der Leitung.
/// </summary>
internal sealed record AnmeldungEntschiedenMail(Guid AnmeldungId, Entscheidung Entscheidung) : IMailAuftrag;

/// <summary>Betreff und Text der Bestätigungs- und Absage-Mail: Deutsch, ohne Anrede und ohne Du/Sie.</summary>
internal static class EntscheidungsMail
{
    public const string BestaetigtAnfang = "Anmeldung bestätigt: ";

    public const string AbgesagtAnfang = "Anmeldung abgesagt: ";

    public static string Betreff(Entscheidung entscheidung, string freizeit) =>
        (entscheidung == Entscheidung.Bestaetigt ? BestaetigtAnfang : AbgesagtAnfang) + freizeit;

    /// <summary>Mail-Adresse und Telefon der Leitung für Absagen, getrennt mit „ · “; ohne beides <c>null</c>.</summary>
    public static string? Kontakt(string? mail, string? telefon)
    {
        var teile = new[] { mail, telefon }.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim()).ToList();
        return teile.Count == 0 ? null : string.Join(" · ", teile);
    }

    public static string Text(Entscheidung entscheidung, AnmeldeArt art, string? vorname, string freizeit, string? kontaktMail, string? kontaktTelefon)
    {
        var name = string.IsNullOrWhiteSpace(vorname) ? "Die angemeldete Person" : vorname.Trim();
        var kontakt = Kontakt(kontaktMail, kontaktTelefon);
        var absageWeg = kontakt is null
            ? "Eine Absage geht nur über die Leitung der Freizeit."
            : $"Eine Absage geht nur über die Leitung: {kontakt}.";
        if (entscheidung == Entscheidung.Bestaetigt)
        {
            var satz = art == AnmeldeArt.Mitarbeitende ? $"{name} arbeitet bei {freizeit} mit." : $"{name} ist bei {freizeit} dabei.";
            return $"""
                Anmeldung bestätigt: {satz}

                {absageWeg}

                Diese Mail hat die Leitung der Freizeit ausgelöst.
                """;
        }

        var fragen = kontakt is null ? "Fragen dazu beantwortet die Leitung der Freizeit." : $"Fragen dazu beantwortet die Leitung: {kontakt}.";
        return $"""
            Anmeldung abgesagt: Die Anmeldung von {name} für {freizeit} ist abgesagt.

            {fragen}

            Diese Mail hat die Leitung der Freizeit ausgelöst.
            """;
    }
}

/// <summary>
/// Baut die Bestätigungs- oder Absage-Mail erst beim Versand (AD-13), unter dem Mandanten aus dem Umschlag: Anmeldung und Freizeit aus
/// Freizeiten, Empfänger über <see cref="IAnmeldeKontakt"/> (Pfeil Freizeiten → Personen), Vorname über <see cref="IPersonenNamen"/>.
/// Ohne Adresse, bei einer gelöschten Person oder einer verschwundenen Anmeldung gibt es keine Mail (<c>null</c>).
/// </summary>
internal sealed class AnmeldungEntschiedenMailAufbau(FreizeitenLeseDbContext db, IAnmeldeKontakt kontakt, IPersonenNamen namen)
    : IMailAufbau<AnmeldungEntschiedenMail>
{
    public async Task<MailInhalt?> AufbauenAsync(AnmeldungEntschiedenMail auftrag, CancellationToken abbruch)
    {
        var anmeldung = await db.Anmeldungen.Where(a => a.Id == auftrag.AnmeldungId).Select(a => new { a.PersonId, a.FreizeitId, a.Art }).SingleOrDefaultAsync(abbruch);
        if (anmeldung is null || anmeldung.PersonId == GeloeschtePerson.Id
                              || await kontakt.MailAsync(anmeldung.PersonId, abbruch) is not { } an)
        {
            return null;
        }

        var freizeit = await db.Freizeiten.Where(f => f.Id == anmeldung.FreizeitId).Select(f => new { f.Name, f.KontaktMail, f.KontaktTelefon }).SingleAsync(abbruch);
        var vorname = (await namen.NamenAsync([anmeldung.PersonId], abbruch)).GetValueOrDefault(anmeldung.PersonId)?.Vorname;
        return new MailInhalt(
            an,
            EntscheidungsMail.Betreff(auftrag.Entscheidung, freizeit.Name),
            EntscheidungsMail.Text(auftrag.Entscheidung, anmeldung.Art, vorname, freizeit.Name, freizeit.KontaktMail, freizeit.KontaktTelefon));
    }
}
