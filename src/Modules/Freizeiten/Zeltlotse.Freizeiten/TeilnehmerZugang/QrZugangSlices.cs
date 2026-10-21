using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using QRCoder;
using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Freizeiten.Daten;
using Zeltlotse.Freizeiten.Domaene;
using Zeltlotse.Freizeiten.Freizeitseite;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Rechte;
using Zeltlotse.SharedKernel.Web;
using Zeltlotse.SharedKernel.Zugangslinks;

namespace Zeltlotse.Freizeiten.TeilnehmerZugang;

/// <summary>
/// Story 6-4 (AD-11): Token, Adresse und QR-Code des Teilnehmer-Zugangs. Das Token entsteht wie beim Anmeldelink (<c>Zugangslink</c>, Zweck
/// <see cref="QrZugang.Zweck"/>) und liegt als Hash und verschlüsselt vor. Die Matrix des QR-Codes erzeugt der Server mit QRCoder (Fehlerkorrektur M, mit
/// Ruhezone von vier Modulen); der Client zeichnet sie nur.
/// </summary>
internal static class QrZugangErzeugen
{
    public static (byte[] Hash, byte[] Geschuetzt) Token(ILinkSchutz schutz, Guid freizeitId)
    {
        var token = Zugangslink.NeuesToken();
        return (Zugangslink.TokenHash(QrZugang.Zweck, token), schutz.Schuetzen(token, freizeitId, QrZugang.Zweck));
    }

    public static string Token(ILinkSchutz schutz, QrZugang zugang) => schutz.Lesen(zugang.TokenGeschuetzt, zugang.FreizeitId, QrZugang.Zweck);

    /// <summary>Die vollständige Adresse des Links: öffentliche Adresse plus <c>/app/teilnehmer/{token}</c>.</summary>
    public static string Adresse(IConfiguration konfiguration, string token) => $"{OeffentlicheAdresse.Lesen(konfiguration)}/app/teilnehmer/{token}";

    /// <summary>Die Matrix des QR-Codes zur Adresse: je Zeile ein Text aus <c>'1'</c> (dunkel) und <c>'0'</c> (hell), mit Ruhezone.</summary>
    public static IReadOnlyList<string> Zeilen(string adresse)
    {
        using var daten = QRCodeGenerator.GenerateQrCode(adresse, QRCodeGenerator.ECCLevel.M);
        return [.. daten.ModuleMatrix.Select(zeile => string.Concat(Enumerable.Range(0, zeile.Length).Select(i => zeile[i] ? '1' : '0')))];
    }

    public static QrZugangAnsicht Ansicht(ILinkSchutz schutz, IConfiguration konfiguration, QrZugang? zugang)
    {
        if (zugang is null)
        {
            return new QrZugangAnsicht(null, null);
        }

        var adresse = Adresse(konfiguration, Token(schutz, zugang));
        return new QrZugangAnsicht(adresse, Zeilen(adresse));
    }
}

/// <summary>
/// Story 6-4: Erzeugt den QR-Link des Teilnehmer-Zugangs oder erneuert ihn (Leitung, nur mit dem Modul Teilnehmer-Zugang): neues Token und neuer Stempel,
/// der bisherige Link und alle damit ausgestellten Gast-Sitzungen sind sofort ungültig. Eine abgeschlossene Freizeit ist nur lesbar (AD-17).
/// </summary>
internal sealed record QrZugangErneuernCommand(Guid FreizeitId)
    : ICommand<QrZugangErneuerung>, IModulCommand<FreizeitenDbContext>, IBenutzerCommand, ISchalterCommand
{
    public FunktionsSchalter Schalter => FunktionsSchalter.TeilnehmerZugang;
}

/// <summary>Ergebnis von <see cref="QrZugangErneuernCommand"/>.</summary>
internal abstract record QrZugangErneuerung
{
    private QrZugangErneuerung()
    {
    }

    public sealed record Erneuert(QrZugang Zugang) : QrZugangErneuerung;

    public sealed record NichtGefunden : QrZugangErneuerung;

    public sealed record Abgeschlossen : QrZugangErneuerung;
}

internal sealed class QrZugangErneuernHandler(FreizeitenDbContext db, ILinkSchutz schutz, TimeProvider uhr)
    : ICommandHandler<QrZugangErneuernCommand, QrZugangErneuerung>
{
    public async ValueTask<QrZugangErneuerung> Handle(QrZugangErneuernCommand command, CancellationToken cancellationToken)
    {
        // Unter dem Mandantenfilter; die Freizeit unter Zeilensperre, damit gleichzeitiges Erneuern nacheinander läuft.
        var freizeit = (await db.Freizeiten
                .FromSql($"SELECT *, xmin FROM freizeiten.freizeit WHERE \"Id\" = {command.FreizeitId} FOR UPDATE")
                .ToListAsync(cancellationToken))
            .SingleOrDefault();
        if (freizeit is null)
        {
            return new QrZugangErneuerung.NichtGefunden();
        }

        if (freizeit.Phase(uhr.GetUtcNow()) == FreizeitPhase.Gelaufen)
        {
            return new QrZugangErneuerung.Abgeschlossen();
        }

        var (hash, geschuetzt) = QrZugangErzeugen.Token(schutz, freizeit.Id);
        var stempel = Kennung.Neu(uhr);
        var zugang = await db.QrZugaenge.SingleOrDefaultAsync(q => q.FreizeitId == freizeit.Id, cancellationToken);
        if (zugang is null)
        {
            zugang = QrZugang.Anlegen(Kennung.Neu(uhr), freizeit.OrganisationId, freizeit.Id, hash, geschuetzt, stempel, uhr.GetUtcNow());
            db.QrZugaenge.Add(zugang);
        }
        else
        {
            zugang.Erneuern(hash, geschuetzt, stempel, uhr.GetUtcNow());
        }

        return new QrZugangErneuerung.Erneuert(zugang);
    }
}

/// <summary>
/// <c>GET /api/freizeiten/{freizeitId}/qr-zugang</c> (Leitung, Modul Teilnehmer-Zugang, Story 6-4): Adresse und QR-Code des Links; gibt es noch keinen,
/// beide Felder <c>null</c>. Auch nach dem Abschluss lesbar. Den Mandanten hat der Host aus der Freizeit gesetzt.
/// </summary>
internal static class QrZugangLadenEndpunkt
{
    public static async Task<Ok<QrZugangAnsicht>> Laden(
        Guid freizeitId, FreizeitenLeseDbContext db, ILinkSchutz schutz, IConfiguration konfiguration, CancellationToken abbruch)
    {
        var zugang = await db.QrZugaenge.SingleOrDefaultAsync(q => q.FreizeitId == freizeitId, abbruch);
        return TypedResults.Ok(QrZugangErzeugen.Ansicht(schutz, konfiguration, zugang));
    }
}

/// <summary>
/// <c>POST /api/freizeiten/{freizeitId}/qr-zugang/erneuern</c> (Leitung, Modul Teilnehmer-Zugang, Story 6-4): erzeugt oder erneuert den Link,
/// <c>200</c> mit der neuen Ansicht; nach dem Abschluss <c>409 freizeit-abgeschlossen</c>.
/// </summary>
internal static class QrZugangErneuernEndpunkt
{
    public static async Task<IResult> Erneuern(Guid freizeitId, ISender sender, ILinkSchutz schutz, IConfiguration konfiguration, CancellationToken abbruch) =>
        await sender.Send(new QrZugangErneuernCommand(freizeitId), abbruch) switch
        {
            QrZugangErneuerung.Erneuert erneuert => TypedResults.Ok(QrZugangErzeugen.Ansicht(schutz, konfiguration, erneuert.Zugang)),
            QrZugangErneuerung.Abgeschlossen => FreizeitseiteProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
}
