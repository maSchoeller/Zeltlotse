using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Zeltlotse.Freizeiten.Daten;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Freizeiten.Domaene;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Web;
using Zeltlotse.SharedKernel.Zugangslinks;
using ApiArt = Zeltlotse.Api.Contracts.Freizeiten.AnmeldelinkArt;

namespace Zeltlotse.Freizeiten.Freizeitseite;

/// <summary>Erzeugt einen Anmeldelink mit neuem Token: Hash mit dem Zweck der Art und das Token verschlüsselt (AD-11).</summary>
internal static class AnmeldelinkErzeugen
{
    public static Anmeldelink Neu(ILinkSchutz schutz, TimeProvider uhr, Guid organisationId, Guid freizeitId, LinkArt art)
    {
        var (hash, geschuetzt) = Token(schutz, freizeitId, art);
        return Anmeldelink.Anlegen(Kennung.Neu(uhr), organisationId, freizeitId, art, hash, geschuetzt, uhr.GetUtcNow());
    }

    public static (byte[] Hash, byte[] Geschuetzt) Token(ILinkSchutz schutz, Guid freizeitId, LinkArt art)
    {
        var token = Zugangslink.NeuesToken();
        var zweck = Anmeldelink.Zweck(art);
        return (Zugangslink.TokenHash(zweck, token), schutz.Schuetzen(token, freizeitId, zweck));
    }

    /// <summary>Die vollständige Adresse des Links: öffentliche Adresse plus <c>/freizeit/{token}</c>.</summary>
    public static string Adresse(IConfiguration konfiguration, string token) => $"{OeffentlicheAdresse.Lesen(konfiguration)}/freizeit/{token}";

    public static string Token(ILinkSchutz schutz, Anmeldelink link) => schutz.Lesen(link.TokenGeschuetzt, link.FreizeitId, Anmeldelink.Zweck(link.Art));

    public static ApiArt Api(LinkArt art) => art switch
    {
        LinkArt.Teilnehmende => ApiArt.Teilnehmende,
        LinkArt.Mitarbeitende => ApiArt.Mitarbeitende,
        _ => throw new ArgumentOutOfRangeException(nameof(art), art, null),
    };

    public static LinkArt Domaene(ApiArt art) => art switch
    {
        ApiArt.Teilnehmende => LinkArt.Teilnehmende,
        ApiArt.Mitarbeitende => LinkArt.Mitarbeitende,
        _ => throw new ArgumentOutOfRangeException(nameof(art), art, null),
    };
}

/// <summary>
/// Erneuert den Anmeldelink einer Art (Leitung, AD-11): neues Token, der bisherige Link ist sofort ungültig. Fehlt der Link (eine
/// Freizeit von vor Story 2-1), entsteht er. Eine abgeschlossene Freizeit ist nur lesbar (AD-17).
/// </summary>
internal sealed record AnmeldelinkErneuernCommand(Guid FreizeitId, LinkArt Art)
    : ICommand<AnmeldelinkErneuerung>, IModulCommand<FreizeitenDbContext>, IBenutzerCommand, IFreizeitCommand;

/// <summary>Ergebnis von <see cref="AnmeldelinkErneuernCommand"/>; bei <see cref="Erneuert"/> das neue Token.</summary>
internal abstract record AnmeldelinkErneuerung
{
    private AnmeldelinkErneuerung()
    {
    }

    public sealed record Erneuert(string Token) : AnmeldelinkErneuerung
    {
        /// <summary>Nie das Token ausgeben (AD-22).</summary>
        public override string ToString() => nameof(Erneuert);
    }

    public sealed record NichtGefunden : AnmeldelinkErneuerung;

    public sealed record Abgeschlossen : AnmeldelinkErneuerung;
}

internal sealed class AnmeldelinkErneuernHandler(FreizeitenDbContext db, ILinkSchutz schutz, TimeProvider uhr)
    : ICommandHandler<AnmeldelinkErneuernCommand, AnmeldelinkErneuerung>
{
    public async ValueTask<AnmeldelinkErneuerung> Handle(AnmeldelinkErneuernCommand command, CancellationToken cancellationToken)
    {
        // Unter dem Mandantenfilter; die Freizeit unter Zeilensperre, damit gleichzeitiges Erneuern nacheinander läuft.
        var freizeit = (await db.Freizeiten
                .FromSql($"SELECT *, xmin FROM freizeiten.freizeit WHERE \"Id\" = {command.FreizeitId} FOR UPDATE")
                .ToListAsync(cancellationToken))
            .SingleOrDefault();
        if (freizeit is null)
        {
            return new AnmeldelinkErneuerung.NichtGefunden();
        }

        if (freizeit.Phase(uhr.GetUtcNow()) == FreizeitPhase.Gelaufen)
        {
            return new AnmeldelinkErneuerung.Abgeschlossen();
        }

        var link = await db.Anmeldelinks.SingleOrDefaultAsync(l => l.FreizeitId == freizeit.Id && l.Art == command.Art, cancellationToken);
        if (link is null)
        {
            link = AnmeldelinkErzeugen.Neu(schutz, uhr, freizeit.OrganisationId, freizeit.Id, command.Art);
            db.Anmeldelinks.Add(link);
        }
        else
        {
            var (hash, geschuetzt) = AnmeldelinkErzeugen.Token(schutz, freizeit.Id, command.Art);
            link.Erneuern(hash, geschuetzt, uhr.GetUtcNow());
        }

        return new AnmeldelinkErneuerung.Erneuert(AnmeldelinkErzeugen.Token(schutz, link));
    }
}

/// <summary>
/// Findet die Freizeit zu einem Anmeldelink (AD-6, AD-11): Die Suche per Token-Hash umgeht den Mandantenfilter, denn hier ist das
/// Token die Quelle des Mandanten-Kontexts (wie bei <c>LeitungAnnehmen</c>). Danach setzt sie den Kontext auf die Organisation des
/// Links und lädt die Freizeit unter dem Filter. <c>null</c> für ein unbekanntes, erneuertes oder falsch geformtes Token, ein Token
/// eines anderen Zwecks und nach dem Freizeitende. Ein Aufruf ist nur eine Ansicht, darum kein Eintrag im Sicherheitsprotokoll.
/// </summary>
internal static class AnmeldelinkSuche
{
    public static Task<(Anmeldelink Link, Freizeit Freizeit)?> FindenAsync(
        FreizeitenLeseDbContext db, MandantenKontext mandant, string? token, DateTimeOffset jetzt, CancellationToken abbruch) =>
        FindenAsync(db.Anmeldelinks, db.Freizeiten, mandant, token, jetzt, abbruch);

    /// <summary>
    /// Dieselbe Suche über die Tabellen eines Command-Handlers (Einlösen beim Absenden einer Anmeldung, Story 2-2); bekommt nur die
    /// Tabellen, nicht den schreibenden DbContext (AD-27).
    /// </summary>
    public static async Task<(Anmeldelink Link, Freizeit Freizeit)?> FindenAsync(
        IQueryable<Anmeldelink> links, IQueryable<Freizeit> freizeiten, MandantenKontext mandant, string? token, DateTimeOffset jetzt, CancellationToken abbruch)
    {
        if (!Zugangslink.KannTokenSein(token))
        {
            return null;
        }

        var teilnehmende = Zugangslink.TokenHash(Anmeldelink.ZweckTeilnehmende, token!);
        var mitarbeitende = Zugangslink.TokenHash(Anmeldelink.ZweckMitarbeitende, token!);
        var link = await links.IgnoreQueryFilters()
            .SingleOrDefaultAsync(l => l.TokenHash == teilnehmende || l.TokenHash == mitarbeitende, abbruch);
        if (link is null || !link.TokenHash.AsSpan().SequenceEqual(link.Art == LinkArt.Teilnehmende ? teilnehmende : mitarbeitende))
        {
            return null;
        }

        mandant.Setzen(link.OrganisationId);
        var freizeit = await freizeiten.SingleOrDefaultAsync(f => f.Id == link.FreizeitId, abbruch);
        if (freizeit is null || jetzt >= FreizeitZeiten.Freizeitende(freizeit.LetzterTag))
        {
            return null;
        }

        return (link, freizeit);
    }
}
