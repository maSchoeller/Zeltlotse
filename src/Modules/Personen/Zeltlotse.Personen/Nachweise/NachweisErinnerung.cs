using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Zeltlotse.Konten.Contracts.Konten;
using Zeltlotse.Personen.Daten;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mail;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Web;

namespace Zeltlotse.Personen.Nachweise;

/// <summary>
/// Mail-Auftrag „Erinnerung an Nachweise“ (AD-13): nur Kennungen. Adresse, Name der Leitung und Text entstehen erst beim Versand
/// (<see cref="NachweisErinnerungMailAufbau"/>), nichts davon steht in der Outbox (AD-4, AD-22).
/// </summary>
internal sealed record NachweisErinnerungMail(Guid PersonId, Guid ErinnertVon) : IMailAuftrag;

/// <summary>
/// Die letzte Erinnerung der Leitung an eine Person, ihre Nachweise zu ergänzen (FR-11, NFR-10): eine Zeile je (Freizeit, Person),
/// höchstens eine Erinnerung je Kalendertag in Europe/Berlin. Jede Erinnerung merkt die Mail über die Outbox vor; verschickt wird
/// nur nach der ausdrücklich bestätigten Aktion der Leitung (AD-13).
/// </summary>
internal sealed class NachweisErinnerung : Aggregat, IOrganisationsEigen
{
    private NachweisErinnerung()
    {
    }

    public Guid OrganisationId { get; private init; }

    public Guid FreizeitId { get; private init; }

    public Guid PersonId { get; private init; }

    /// <summary>Die Leitung, die zuletzt erinnert hat.</summary>
    public Guid ErinnertVon { get; private set; }

    public DateTimeOffset ErinnertAm { get; private set; }

    /// <summary>Die erste Erinnerung derselben Freizeit an die Person; legt den Mail-Auftrag ab.</summary>
    public static NachweisErinnerung Neu(Guid id, Guid organisationId, Guid freizeitId, Guid personId, Guid von, DateTimeOffset jetzt)
    {
        var erinnerung = new NachweisErinnerung { Id = id, OrganisationId = organisationId, FreizeitId = freizeitId, PersonId = personId };
        erinnerung.Setzen(von, jetzt);
        return erinnerung;
    }

    /// <summary>Schon am selben Kalendertag (Europe/Berlin) erinnert?</summary>
    public bool HeuteSchonErinnert(DateTimeOffset jetzt) => NachweisRegeln.Tag(ErinnertAm) == NachweisRegeln.Tag(jetzt);

    /// <summary>Eine weitere Erinnerung; <c>false</c>, wenn heute schon erinnert wurde (dann keine Mail).</summary>
    public bool Erinnern(Guid von, DateTimeOffset jetzt)
    {
        if (HeuteSchonErinnert(jetzt))
        {
            return false;
        }

        Setzen(von, jetzt);
        return true;
    }

    private void Setzen(Guid von, DateTimeOffset jetzt)
    {
        ErinnertVon = von;
        ErinnertAm = jetzt;
        MailVormerken(new NachweisErinnerungMail(PersonId, von));
    }
}

/// <summary>
/// Erinnern (Leitung im Freizeit-Kontext, FR-11): nur für Personen mit Zeile in der eigenen Liste der Freizeit-Rollen dieser
/// Freizeit, nicht für sich selbst. Stellt in einem Command die offene Einsicht mit der Logik von „Einsicht anfragen“ sicher
/// (eine vorhandene bleibt unverändert), speichert die Erinnerung und merkt die Mail vor.
/// </summary>
internal sealed record NachweisErinnern(Guid OrganisationId, Guid FreizeitId, Guid AnfragendePersonId, Guid? PersonId)
    : ICommand<ErinnerungErgebnis>, IModulCommand<PersonenDbContext>, IBenutzerCommand;

/// <summary>Ergebnis von <see cref="NachweisErinnern"/>.</summary>
internal abstract record ErinnerungErgebnis
{
    private ErinnerungErgebnis()
    {
    }

    /// <summary>Erinnert: <c>200 {erinnertAm}</c>.</summary>
    public sealed record Erinnert(DateTimeOffset ErinnertAm) : ErinnerungErgebnis;

    /// <summary>Keine Rolle in der Freizeit oder die anfragende Person selbst: <c>404</c>.</summary>
    public sealed record NichtGefunden : ErinnerungErgebnis;

    /// <summary>Heute schon erinnert (Europe/Berlin): <c>409</c>.</summary>
    public sealed record SchonHeute : ErinnerungErgebnis;
}

internal sealed class NachweisErinnernValidator : AbstractValidator<NachweisErinnern>
{
    public NachweisErinnernValidator()
    {
        RuleFor(c => c.PersonId).NotNull().WithMessage("Person fehlt.");
    }
}

internal sealed class NachweisErinnernHandler(PersonenDbContext db, TimeProvider uhr) : ICommandHandler<NachweisErinnern, ErinnerungErgebnis>
{
    public async ValueTask<ErinnerungErgebnis> Handle(NachweisErinnern command, CancellationToken cancellationToken)
    {
        var personId = command.PersonId!.Value;
        if (personId == command.AnfragendePersonId
            || !await EinsichtAnfragenHandler.AnfragbarAsync(db.FreizeitRollen, command.FreizeitId, personId, cancellationToken))
        {
            return new ErinnerungErgebnis.NichtGefunden();
        }

        // Gleichzeitige Erinnerungen derselben Freizeit an dieselbe Person laufen nacheinander; dahinter steht der eindeutige Index.
        await NachweisRegeln.ErinnerungSperrenAsync(db, command.FreizeitId, personId, cancellationToken);
        var jetzt = uhr.GetUtcNow();
        var erinnerung = await db.Erinnerungen.SingleOrDefaultAsync(e => e.FreizeitId == command.FreizeitId && e.PersonId == personId, cancellationToken);
        if (erinnerung is not null && erinnerung.HeuteSchonErinnert(jetzt))
        {
            return new ErinnerungErgebnis.SchonHeute();
        }

        await EinsichtSicherstellen.VonHandAsync(
            db, db.Einsichten, command.OrganisationId, personId, command.FreizeitId, command.AnfragendePersonId, uhr, cancellationToken);
        if (erinnerung is null)
        {
            db.Erinnerungen.Add(NachweisErinnerung.Neu(Kennung.Neu(uhr), command.OrganisationId, command.FreizeitId, personId, command.AnfragendePersonId, jetzt));
        }
        else
        {
            erinnerung.Erinnern(command.AnfragendePersonId, jetzt);
        }

        return new ErinnerungErgebnis.Erinnert(jetzt);
    }
}

/// <summary>Betreff und Text der Erinnerungs-Mail: Deutsch, ohne Anrede und ohne Du/Sie, ohne Angaben zu einzelnen Nachweisen.</summary>
internal static class ErinnerungsMail
{
    public const string Betreff = "Erinnerung: Nachweise in Zeltlotse";

    /// <summary>Die Seite „Nachweise“ im Konto der Web-App.</summary>
    public static string Link(string oeffentlicheAdresse) => $"{oeffentlicheAdresse}/app/konto/nachweise";

    public static string Text(string oeffentlicheAdresse, string? vorname, string? nachname)
    {
        var name = string.Join(" ", new[] { vorname, nachname }.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim()));
        return $"""
            {(name.Length == 0 ? "Eine Person" : name)} aus der Leitung einer Freizeit bittet darum, die Nachweise in Zeltlotse zu ergänzen und die Anfrage zur Einsicht freizugeben:

            {Link(oeffentlicheAdresse)}

            Diese Mail hat die Leitung ausgelöst.
            """;
    }
}

/// <summary>
/// Baut die Erinnerungs-Mail erst beim Versand (AD-13): Adresse aus dem Konto der Person (Pfeil Personen → Konten), Name der
/// Leitung aus dem Profil. Ohne Konto gibt es keine Mail (<c>null</c>).
/// </summary>
internal sealed class NachweisErinnerungMailAufbau(IKontoLesen konten, PersonenLeseDbContext db, IConfiguration konfiguration)
    : IMailAufbau<NachweisErinnerungMail>
{
    public async Task<MailInhalt?> AufbauenAsync(NachweisErinnerungMail auftrag, CancellationToken abbruch)
    {
        if (await konten.MailDerPersonAsync(auftrag.PersonId, abbruch) is not { } mail)
        {
            return null;
        }

        var leitung = await db.Personen.Where(p => p.Id == auftrag.ErinnertVon).Select(p => new { p.Vorname, p.Nachname }).SingleOrDefaultAsync(abbruch);
        return new MailInhalt(
            mail,
            ErinnerungsMail.Betreff,
            ErinnerungsMail.Text(OeffentlicheAdresse.Lesen(konfiguration), leitung?.Vorname, leitung?.Nachname));
    }
}
