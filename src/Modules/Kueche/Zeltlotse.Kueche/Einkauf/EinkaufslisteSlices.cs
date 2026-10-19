using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Kueche;
using Zeltlotse.Freizeiten.Contracts.Personen;
using Zeltlotse.Freizeiten.Contracts.Zeitpunkte;
using Zeltlotse.Kueche.Daten;
using Zeltlotse.Kueche.Rezepte;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.SharedKernel.Rechte;

namespace Zeltlotse.Kueche.Einkauf;

// ---------- Hand-Menge ----------

/// <summary>
/// Story 4-2, CAP-29: Setzt die Menge einer Zutat in einem Gericht von Hand oder nimmt sie zurück (leer). Konfliktregel nach AD-19: Der alte Wert ist der
/// angezeigte Text (Hand-Menge oder berechnete Menge); weicht der aktuelle davon ab, ist das Ergebnis <see cref="HandMengeAenderung.Konflikt"/>. Story 4-3,
/// FR-51: ebenso <c>freigegeben</c> und <c>gekauft</c> des Postens (<see cref="PostenStand"/>, ohne Zeile <c>"false"</c>).
/// </summary>
internal sealed record HandMengeAendernCommand(Guid FreizeitId, Guid GerichtId, Guid ZutatId, HandMengeFeld Feld, uint Version, string? AlterWert, string? NeuerWert)
    : ICommand<HandMengeAenderung>, IModulCommand<KuecheDbContext>, IBenutzerCommand, ISchalterCommand
{
    public FunktionsSchalter Schalter => FunktionsSchalter.Verpflegung;
}

/// <summary>Ergebnis von <see cref="HandMengeAendernCommand"/>; bei <see cref="Gespeichert"/> die Hand-Menge (ohne: zurück zur berechneten) und der angezeigte Text.</summary>
internal abstract record HandMengeAenderung
{
    private HandMengeAenderung()
    {
    }

    public sealed record Gespeichert(MengeVonHand? Hand, string? Angezeigt) : HandMengeAenderung;

    /// <summary>Story 4-3: <c>freigegeben</c> oder <c>gekauft</c> gespeichert; der verfolgte Stand (nach dem Speichern mit Version).</summary>
    public sealed record StandGespeichert(PostenStand Stand) : HandMengeAenderung;

    public sealed record Konflikt : HandMengeAenderung;

    public sealed record Abgeschlossen : HandMengeAenderung;

    public sealed record NichtGefunden : HandMengeAenderung;
}

internal sealed class HandMengeAendernValidator : AbstractValidator<HandMengeAendernCommand>
{
    public HandMengeAendernValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (!Enum.IsDefined(c.Feld))
            {
                kontext.AddFailure(new ValidationFailure("feld", "Unbekanntes Feld."));
                return;
            }

            var fehler = c.Feld == HandMengeFeld.Menge ? KuecheWerte.MengeFehler(c.NeuerWert) : KuecheWerte.JaNeinFehler(c.NeuerWert);
            if (fehler is not null)
            {
                kontext.AddFailure(new ValidationFailure(HandMengeFelder.JsonName(c.Feld), fehler));
            }
        });
}

internal sealed class HandMengeAendernHandler(
    KuecheDbContext db, IMandantenKontext mandant, IFreizeitZeitpunkte zeitpunkte, IFreizeitPersonenzahlLesen personenzahl, TimeProvider uhr)
    : ICommandHandler<HandMengeAendernCommand, HandMengeAenderung>
{
    public async ValueTask<HandMengeAenderung> Handle(HandMengeAendernCommand command, CancellationToken cancellationToken)
    {
        if (mandant.OrganisationId is not { } organisationId
            || !(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return new HandMengeAenderung.NichtGefunden();
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return new HandMengeAenderung.Abgeschlossen();
        }

        // Zeilensperre am Gericht: Hand-Mengen, Tausch und Entfernen desselben Gerichts laufen nacheinander (auch wenn es die Hand-Menge noch nicht gibt).
        var gericht = await db.GerichtSperrenAsync(command.FreizeitId, command.GerichtId, cancellationToken);
        if (gericht is null)
        {
            return new HandMengeAenderung.NichtGefunden();
        }

        var zutat = await db.Zutaten.SingleOrDefaultAsync(z => z.Id == command.ZutatId && z.RezeptId == gericht.RezeptId, cancellationToken);
        if (zutat is null)
        {
            return new HandMengeAenderung.NichtGefunden();
        }

        if (command.Feld != HandMengeFeld.Menge)
        {
            return await StandAendernAsync(command, organisationId, gericht, zutat.Id, cancellationToken);
        }

        var rezept = await db.Rezepte.SingleAsync(r => r.Id == gericht.RezeptId, cancellationToken);
        var hand = await db.MengenVonHand.SingleOrDefaultAsync(h => h.GerichtId == gericht.Id && h.ZutatId == zutat.Id, cancellationToken);
        var berechnet = KuecheWerte.Hochrechnen(zutat.Menge, rezept.BasisPersonen, await personenzahl.AnzahlAsync(command.FreizeitId, cancellationToken));
        if (EinkaufRechnen.MengeText(hand?.Menge ?? berechnet) != Vergleichswert(command.AlterWert))
        {
            return new HandMengeAenderung.Konflikt();
        }

        // Die Hand-Menge gehört zum Stand des Gerichts: Das Gericht bekommt eine neue Version, damit eine vorher geladene Tausch-Vorschau veraltet ist.
        db.Entry(gericht).Property(g => g.ProgrammpunktId).IsModified = true;
        if (KuecheWerte.MengeLesen(command.NeuerWert) is not { } neu)
        {
            if (hand is not null)
            {
                db.MengenVonHand.Remove(hand);
            }

            return new HandMengeAenderung.Gespeichert(null, EinkaufRechnen.MengeText(berechnet));
        }

        if (hand is null)
        {
            hand = MengeVonHand.Anlegen(Guid.CreateVersion7(uhr.GetUtcNow()), organisationId, command.FreizeitId, gericht.Id, zutat.Id, neu);
            db.MengenVonHand.Add(hand);
        }
        else
        {
            hand.Setzen(neu);
        }

        return new HandMengeAenderung.Gespeichert(hand, EinkaufRechnen.MengeText(hand.Menge));
    }

    /// <summary>
    /// Story 4-3: <c>freigegeben</c> oder <c>gekauft</c> eines berechneten Postens. Das Gericht ist schon gesperrt: Zwei Änderungen am selben Posten laufen
    /// nacheinander, die Stand-Zeile entsteht so genau einmal. Auch hier bekommt das Gericht eine neue Version (eine ältere Tausch-Vorschau ist veraltet).
    /// </summary>
    private async Task<HandMengeAenderung> StandAendernAsync(HandMengeAendernCommand command, Guid organisationId, Gericht gericht, Guid zutatId, CancellationToken abbruch)
    {
        var feld = command.Feld == HandMengeFeld.Freigegeben ? StandFeld.Freigegeben : StandFeld.Gekauft;
        // Ohne Zeile gilt der Stand eines neuen Postens (beides false); die Konfliktregel (AD-19) prüft FeldAendern.
        var stand = await db.PostenStaende.SingleOrDefaultAsync(s => s.GerichtId == gericht.Id && s.ZutatId == zutatId, abbruch)
                    ?? PostenStand.Anlegen(Guid.CreateVersion7(uhr.GetUtcNow()), organisationId, command.FreizeitId, gericht.Id, zutatId);
        if (!stand.FeldAendern(feld, command.AlterWert, command.NeuerWert))
        {
            return new HandMengeAenderung.Konflikt();
        }

        if (db.Entry(stand).State == EntityState.Detached)
        {
            db.PostenStaende.Add(stand);
        }

        db.Entry(gericht).Property(g => g.ProgrammpunktId).IsModified = true;
        return new HandMengeAenderung.StandGespeichert(stand);
    }

    private static string? Vergleichswert(string? alterWert) =>
        KuecheWerte.MengeLesen(alterWert) is { } menge ? ZutatFelder.ZahlText(menge) : KuecheWerte.TextOderNull(alterWert);
}

/// <summary>
/// <c>PATCH /api/freizeiten/{freizeitId}/kueche/einkaufsliste/gerichte/{gerichtId}/zutaten/{zutatId}</c> (Leitung und Team, Modul Verpflegung, AD-19):
/// Körper <c>{ "version": n, "alterWert": …, "menge"|"freigegeben"|"gekauft": neu }</c>. <c>200 { "version", "menge" }</c> mit dem angezeigten Wert (leer:
/// die berechnete Menge, Version 0); Story 4-3: <c>200 { "version", "freigegeben"|"gekauft": "true"|"false" }</c>. Ungültige Menge oder ein anderer
/// Ja/Nein-Wert als <c>"true"</c>/<c>"false"</c> <c>400</c> am Feld; anderer angezeigter Wert <c>409 konflikt</c>; abgeschlossene Freizeit
/// <c>409 freizeit-abgeschlossen</c>; Gericht oder Zutat (nicht im Rezept des Gerichts) unbekannt oder aus einer anderen Freizeit <c>404</c>.
/// </summary>
internal static class HandMengeAendernEndpunkt
{
    public static async Task<IResult> Aendern(Guid freizeitId, Guid gerichtId, Guid zutatId, JsonElement koerper, ISender sender, CancellationToken abbruch)
    {
        var (version, alterWert, feld, neuerWert) = FeldPatch.Lesen<HandMengeFeld>(koerper, HandMengeFelder.JsonName);
        return await sender.Send(new HandMengeAendernCommand(freizeitId, gerichtId, zutatId, feld, version, alterWert, neuerWert), abbruch) switch
        {
            HandMengeAenderung.Gespeichert gespeichert => TypedResults.Ok(new Dictionary<string, object?>
            {
                ["version"] = gespeichert.Hand?.Version ?? 0u,
                [HandMengeFelder.Menge] = gespeichert.Angezeigt,
            }),
            HandMengeAenderung.StandGespeichert stand => TypedResults.Ok(new Dictionary<string, object?>
            {
                ["version"] = stand.Stand.Version,
                [HandMengeFelder.JsonName(feld)] = PostenStand.Wert(stand.Stand, feld == HandMengeFeld.Freigegeben ? StandFeld.Freigegeben : StandFeld.Gekauft),
            }),
            HandMengeAenderung.Konflikt => KuecheProbleme.Konflikt(),
            HandMengeAenderung.Abgeschlossen => KuecheProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
    }
}

// ---------- Hand-Posten anlegen ----------

/// <summary>Story 4-2, FR-49: Ergänzt die Einkaufsliste um einen Hand-Posten (Leitung und Team, Modul Verpflegung). Nur die Bezeichnung ist Pflicht.</summary>
internal sealed record EinkaufspostenAnlegenCommand(Guid FreizeitId, string? Bezeichnung, string? Menge, string? Einheit, string? Tag)
    : ICommand<EinkaufspostenAnlegen>, IModulCommand<KuecheDbContext>, IBenutzerCommand, ISchalterCommand
{
    public FunktionsSchalter Schalter => FunktionsSchalter.Verpflegung;
}

/// <summary>Ergebnis von <see cref="EinkaufspostenAnlegenCommand"/>; bei <see cref="Angelegt"/> der verfolgte Posten (nach dem Speichern mit Version).</summary>
internal abstract record EinkaufspostenAnlegen
{
    private EinkaufspostenAnlegen()
    {
    }

    public sealed record Angelegt(Einkaufsposten Posten) : EinkaufspostenAnlegen;

    public sealed record Abgeschlossen : EinkaufspostenAnlegen;

    public sealed record NichtGefunden : EinkaufspostenAnlegen;
}

internal sealed class EinkaufspostenAnlegenValidator : AbstractValidator<EinkaufspostenAnlegenCommand>
{
    public EinkaufspostenAnlegenValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            foreach (var (feld, fehler) in new[]
                     {
                         (EinkaufspostenFelder.Bezeichnung, KuecheWerte.PflichtFehler(c.Bezeichnung, EinkaufspostenFelder.BezeichnungMaximaleLaenge, "Bezeichnung fehlt.")),
                         (EinkaufspostenFelder.Menge, KuecheWerte.MengeFehler(c.Menge)),
                         (EinkaufspostenFelder.Einheit, KuecheWerte.LaengeFehler(c.Einheit, EinkaufspostenFelder.EinheitMaximaleLaenge)),
                         (EinkaufspostenFelder.Tag, KuecheWerte.TagFehler(c.Tag)),
                     })
            {
                if (fehler is not null)
                {
                    kontext.AddFailure(new ValidationFailure(feld, fehler));
                }
            }
        });
}

internal sealed class EinkaufspostenAnlegenHandler(
    KuecheDbContext db, IMandantenKontext mandant, IFreizeitZeitpunkte zeitpunkte, IFreizeitTageLesen tageLesen, TimeProvider uhr)
    : ICommandHandler<EinkaufspostenAnlegenCommand, EinkaufspostenAnlegen>
{
    public async ValueTask<EinkaufspostenAnlegen> Handle(EinkaufspostenAnlegenCommand command, CancellationToken cancellationToken)
    {
        if (mandant.OrganisationId is not { } organisationId
            || !(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return new EinkaufspostenAnlegen.NichtGefunden();
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return new EinkaufspostenAnlegen.Abgeschlossen();
        }

        var tag = KuecheWerte.TagLesen(command.Tag);
        await EinkaufTage.PruefenAsync(tageLesen, command.FreizeitId, tag, cancellationToken);
        var posten = Einkaufsposten.Anlegen(
            Guid.CreateVersion7(uhr.GetUtcNow()), organisationId, command.FreizeitId, command.Bezeichnung!, KuecheWerte.MengeLesen(command.Menge), command.Einheit, tag);
        db.Einkaufsposten.Add(posten);
        return new EinkaufspostenAnlegen.Angelegt(posten);
    }
}

/// <summary>Story 4-2: Ein Tag eines Hand-Postens gehört zur Freizeit, sonst <c>400</c> am Feld <c>tag</c>.</summary>
internal static class EinkaufTage
{
    public static async Task PruefenAsync(IFreizeitTageLesen tageLesen, Guid freizeitId, DateOnly? tag, CancellationToken abbruch)
    {
        if (tag is { } wert
            && !((await tageLesen.TageAsync([freizeitId], abbruch)).TryGetValue(freizeitId, out var tage) && tage.Enthaelt(wert)))
        {
            throw GerichtFehler.TagAusserhalb();
        }
    }
}

/// <summary>
/// <c>POST /api/freizeiten/{freizeitId}/kueche/einkaufsliste/posten</c> (Leitung und Team, Modul Verpflegung): Körper <see cref="EinkaufspostenNeu"/>.
/// <c>201</c> mit dem Posten; Bezeichnung fehlt oder zu lang, Menge ungültig, Einheit zu lang, Tag kein Datum oder außerhalb der Freizeit <c>400</c> am
/// Feld; abgeschlossene Freizeit <c>409 freizeit-abgeschlossen</c>.
/// </summary>
internal static class EinkaufspostenAnlegenEndpunkt
{
    public static async Task<IResult> Anlegen(Guid freizeitId, EinkaufspostenNeu koerper, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new EinkaufspostenAnlegenCommand(freizeitId, koerper.Bezeichnung, koerper.Menge, koerper.Einheit, koerper.Tag), abbruch) switch
        {
            EinkaufspostenAnlegen.Angelegt angelegt => TypedResults.Created($"/api/freizeiten/{freizeitId}/kueche/einkaufsliste", EinkaufRechnen.AlsApi(angelegt.Posten)),
            EinkaufspostenAnlegen.Abgeschlossen => KuecheProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
}

// ---------- Hand-Posten bearbeiten ----------

/// <summary>Story 4-2: Ändert genau ein Feld eines Hand-Postens (AD-19); eine abgeschlossene Freizeit ist nur lesbar.</summary>
internal sealed record EinkaufspostenFeldAendernCommand(Guid FreizeitId, Guid PostenId, EinkaufspostenFeld Feld, uint Version, string? AlterWert, string? NeuerWert)
    : ICommand<EinkaufspostenAenderung>, IModulCommand<KuecheDbContext>, IBenutzerCommand, ISchalterCommand
{
    public FunktionsSchalter Schalter => FunktionsSchalter.Verpflegung;
}

/// <summary>Ergebnis von <see cref="EinkaufspostenFeldAendernCommand"/>; bei <see cref="Gespeichert"/> der verfolgte Posten.</summary>
internal abstract record EinkaufspostenAenderung
{
    private EinkaufspostenAenderung()
    {
    }

    public sealed record Gespeichert(Einkaufsposten Posten) : EinkaufspostenAenderung;

    public sealed record Konflikt : EinkaufspostenAenderung;

    public sealed record Abgeschlossen : EinkaufspostenAenderung;

    public sealed record NichtGefunden : EinkaufspostenAenderung;
}

internal sealed class EinkaufspostenFeldAendernValidator : AbstractValidator<EinkaufspostenFeldAendernCommand>
{
    public EinkaufspostenFeldAendernValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (!Enum.IsDefined(c.Feld))
            {
                kontext.AddFailure(new ValidationFailure("feld", "Unbekanntes Feld."));
                return;
            }

            var fehler = c.Feld switch
            {
                EinkaufspostenFeld.Bezeichnung => KuecheWerte.PflichtFehler(c.NeuerWert, EinkaufspostenFelder.BezeichnungMaximaleLaenge, "Bezeichnung fehlt."),
                EinkaufspostenFeld.Menge => KuecheWerte.MengeFehler(c.NeuerWert),
                EinkaufspostenFeld.Einheit => KuecheWerte.LaengeFehler(c.NeuerWert, EinkaufspostenFelder.EinheitMaximaleLaenge),
                EinkaufspostenFeld.Tag => KuecheWerte.TagFehler(c.NeuerWert),
                _ => KuecheWerte.JaNeinFehler(c.NeuerWert),
            };
            if (fehler is not null)
            {
                kontext.AddFailure(new ValidationFailure(EinkaufspostenFelder.JsonName(c.Feld), fehler));
            }
        });
}

internal sealed class EinkaufspostenFeldAendernHandler(KuecheDbContext db, IFreizeitZeitpunkte zeitpunkte, IFreizeitTageLesen tageLesen)
    : ICommandHandler<EinkaufspostenFeldAendernCommand, EinkaufspostenAenderung>
{
    public async ValueTask<EinkaufspostenAenderung> Handle(EinkaufspostenFeldAendernCommand command, CancellationToken cancellationToken)
    {
        if (!(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return new EinkaufspostenAenderung.NichtGefunden();
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return new EinkaufspostenAenderung.Abgeschlossen();
        }

        var posten = await db.PostenSperrenAsync(command.FreizeitId, command.PostenId, cancellationToken);
        if (posten is null)
        {
            return new EinkaufspostenAenderung.NichtGefunden();
        }

        if (command.Feld == EinkaufspostenFeld.Tag)
        {
            await EinkaufTage.PruefenAsync(tageLesen, command.FreizeitId, KuecheWerte.TagLesen(command.NeuerWert), cancellationToken);
        }

        return posten.FeldAendern(command.Feld, command.AlterWert, command.NeuerWert)
            ? new EinkaufspostenAenderung.Gespeichert(posten)
            : new EinkaufspostenAenderung.Konflikt();
    }
}

/// <summary>
/// <c>PATCH /api/freizeiten/{freizeitId}/kueche/einkaufsliste/posten/{postenId}</c> (Leitung und Team, Modul Verpflegung, AD-19): Körper
/// <c>{ "version": n, "alterWert": …, "bezeichnung"|"menge"|"einheit"|"tag"|"freigegeben"|"gekauft": neu }</c> (Story 4-3: Ja/Nein als
/// <c>"true"</c>/<c>"false"</c>). <c>200 { "version", "&lt;feld&gt;" }</c> normalisiert; ungültiger
/// Wert, Tag außerhalb der Freizeit, kein oder mehr als ein Feld <c>400</c>; anderer Wert desselben Felds <c>409 konflikt</c>; abgeschlossene Freizeit
/// <c>409 freizeit-abgeschlossen</c>; unbekannt oder aus einer anderen Freizeit <c>404</c>.
/// </summary>
internal static class EinkaufspostenFeldAendernEndpunkt
{
    public static async Task<IResult> Aendern(Guid freizeitId, Guid postenId, JsonElement koerper, ISender sender, CancellationToken abbruch)
    {
        var (version, alterWert, feld, neuerWert) = FeldPatch.Lesen<EinkaufspostenFeld>(koerper, EinkaufspostenFelder.JsonName);
        return await sender.Send(new EinkaufspostenFeldAendernCommand(freizeitId, postenId, feld, version, alterWert, neuerWert), abbruch) switch
        {
            EinkaufspostenAenderung.Gespeichert gespeichert => TypedResults.Ok(new Dictionary<string, object?>
            {
                ["version"] = gespeichert.Posten.Version,
                [EinkaufspostenFelder.JsonName(feld)] = gespeichert.Posten.Wert(feld),
            }),
            EinkaufspostenAenderung.Konflikt => KuecheProbleme.Konflikt(),
            EinkaufspostenAenderung.Abgeschlossen => KuecheProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
    }
}

// ---------- Tages-Freigabe (Story 4-3) ----------

/// <summary>
/// Story 4-3, FR-51: Setzt <c>freigegeben</c> für alle Posten einer Tagesgruppe der Einkaufsliste: die berechneten Posten der Gerichte an Mahlzeiten dieses
/// Tags und die Hand-Posten mit diesem Tag (<see cref="Tag"/> <c>null</c>: nur die Hand-Posten ohne Tag). Idempotent; <c>gekauft</c> bleibt unberührt.
/// </summary>
internal sealed record EinkaufFreigebenCommand(Guid FreizeitId, string? Tag, bool? Freigegeben)
    : ICommand<KuecheFreigabe>, IModulCommand<KuecheDbContext>, IBenutzerCommand, ISchalterCommand
{
    public FunktionsSchalter Schalter => FunktionsSchalter.Verpflegung;
}

/// <summary>Ergebnis von <see cref="EinkaufFreigebenCommand"/>.</summary>
internal enum KuecheFreigabe
{
    Gespeichert,
    Abgeschlossen,
    NichtGefunden,
}

internal sealed class EinkaufFreigebenValidator : AbstractValidator<EinkaufFreigebenCommand>
{
    public EinkaufFreigebenValidator() =>
        RuleFor(c => c).Custom((c, kontext) =>
        {
            if (KuecheWerte.TagFehler(c.Tag) is { } fehler)
            {
                kontext.AddFailure(new ValidationFailure(EinkaufspostenFelder.Tag, fehler));
            }

            if (c.Freigegeben is null)
            {
                kontext.AddFailure(new ValidationFailure(EinkaufspostenFelder.Freigegeben, "Freigegeben fehlt."));
            }
        });
}

internal sealed class EinkaufFreigebenHandler(
    KuecheDbContext db, IMandantenKontext mandant, IFreizeitZeitpunkte zeitpunkte, IFreizeitTageLesen tageLesen, EinkaufAbfrage abfrage, TimeProvider uhr)
    : ICommandHandler<EinkaufFreigebenCommand, KuecheFreigabe>
{
    public async ValueTask<KuecheFreigabe> Handle(EinkaufFreigebenCommand command, CancellationToken cancellationToken)
    {
        if (mandant.OrganisationId is not { } organisationId
            || !(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return KuecheFreigabe.NichtGefunden;
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return KuecheFreigabe.Abgeschlossen;
        }

        var tag = KuecheWerte.TagLesen(command.Tag);
        await EinkaufTage.PruefenAsync(tageLesen, command.FreizeitId, tag, cancellationToken);
        var freigegeben = command.Freigegeben!.Value;

        if (tag is { } wert)
        {
            var punkte = (await abfrage.MahlzeitenAsync(command.FreizeitId, cancellationToken)).Where(m => m.Tag == wert).Select(m => m.ProgrammpunktId).ToList();
            // Die Gerichte des Tags nach Kennung gesperrt (FOR UPDATE): gleiche Reihenfolge wie jede andere Tages-Freigabe, kein Deadlock; ein PATCH am
            // selben Gericht wartet.
            var gerichte = await db.GerichteSperrenAsync(command.FreizeitId, punkte, cancellationToken);
            var rezeptIds = gerichte.Select(g => g.RezeptId).Distinct().ToList();
            var gerichtIds = gerichte.Select(g => g.Id).ToList();
            var zutaten = (await db.Zutaten.Where(z => rezeptIds.Contains(z.RezeptId)).ToListAsync(cancellationToken)).ToLookup(z => z.RezeptId);
            var staende = await db.PostenStaende.Where(s => gerichtIds.Contains(s.GerichtId)).ToListAsync(cancellationToken);
            var jetzt = uhr.GetUtcNow();
            foreach (var gericht in gerichte)
            {
                var geaendert = false;
                foreach (var zutat in zutaten[gericht.RezeptId])
                {
                    var stand = staende.FirstOrDefault(s => s.GerichtId == gericht.Id && s.ZutatId == zutat.Id);
                    if ((stand?.Freigegeben ?? false) == freigegeben)
                    {
                        continue;
                    }

                    if (stand is null)
                    {
                        stand = PostenStand.Anlegen(Guid.CreateVersion7(jetzt), organisationId, command.FreizeitId, gericht.Id, zutat.Id);
                        db.PostenStaende.Add(stand);
                    }

                    stand.Freigeben(freigegeben);
                    geaendert = true;
                }

                // Wie beim PATCH: Eine Änderung am Stand gibt dem Gericht eine neue Version, eine ältere Tausch-Vorschau ist veraltet.
                if (geaendert)
                {
                    db.Entry(gericht).Property(g => g.ProgrammpunktId).IsModified = true;
                }
            }
        }

        foreach (var posten in await db.PostenAmTagSperrenAsync(command.FreizeitId, tag, cancellationToken))
        {
            if (posten.Freigegeben != freigegeben)
            {
                posten.Freigeben(freigegeben);
            }
        }

        return KuecheFreigabe.Gespeichert;
    }
}

/// <summary>
/// <c>POST /api/freizeiten/{freizeitId}/kueche/einkaufsliste/freigabe</c> (Leitung und Team, Modul Verpflegung, Story 4-3): Körper
/// <see cref="EinkaufFreigabe"/>. <c>204</c>; Tag kein Datum oder außerhalb der Freizeit <c>400</c> am Feld <c>tag</c>, ohne <c>freigegeben</c>
/// <c>400</c> am Feld <c>freigegeben</c>; abgeschlossene Freizeit <c>409 freizeit-abgeschlossen</c>.
/// </summary>
internal static class EinkaufFreigebenEndpunkt
{
    public static async Task<IResult> Freigeben(Guid freizeitId, EinkaufFreigabe koerper, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new EinkaufFreigebenCommand(freizeitId, koerper.Tag, koerper.Freigegeben), abbruch) switch
        {
            KuecheFreigabe.Gespeichert => TypedResults.NoContent(),
            KuecheFreigabe.Abgeschlossen => KuecheProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
}

// ---------- Hand-Posten löschen ----------

/// <summary>Story 4-2: Entfernt einen Hand-Posten (Leitung und Team, Modul Verpflegung); eine abgeschlossene Freizeit ist nur lesbar.</summary>
internal sealed record EinkaufspostenLoeschenCommand(Guid FreizeitId, Guid PostenId)
    : ICommand<KuecheLoeschen>, IModulCommand<KuecheDbContext>, IBenutzerCommand, ISchalterCommand
{
    public FunktionsSchalter Schalter => FunktionsSchalter.Verpflegung;
}

internal sealed class EinkaufspostenLoeschenHandler(KuecheDbContext db, IFreizeitZeitpunkte zeitpunkte) : ICommandHandler<EinkaufspostenLoeschenCommand, KuecheLoeschen>
{
    public async ValueTask<KuecheLoeschen> Handle(EinkaufspostenLoeschenCommand command, CancellationToken cancellationToken)
    {
        if (!(await zeitpunkte.ZeitpunkteAsync([command.FreizeitId], cancellationToken)).TryGetValue(command.FreizeitId, out var freizeit))
        {
            return KuecheLoeschen.NichtGefunden;
        }

        if (freizeit.Phase == FreizeitPhase.Gelaufen)
        {
            return KuecheLoeschen.Abgeschlossen;
        }

        // Zeilensperre: Ein gleichzeitiges Löschen wartet und findet danach nichts mehr (404 statt 500).
        var posten = await db.PostenSperrenAsync(command.FreizeitId, command.PostenId, cancellationToken);
        if (posten is null)
        {
            return KuecheLoeschen.NichtGefunden;
        }

        db.Einkaufsposten.Remove(posten);
        return KuecheLoeschen.Geloescht;
    }
}

/// <summary>
/// <c>DELETE /api/freizeiten/{freizeitId}/kueche/einkaufsliste/posten/{postenId}</c> (Leitung und Team, Modul Verpflegung): <c>204</c>; abgeschlossene
/// Freizeit <c>409 freizeit-abgeschlossen</c>; unbekannt oder aus einer anderen Freizeit <c>404</c>.
/// </summary>
internal static class EinkaufspostenLoeschenEndpunkt
{
    public static async Task<IResult> Loeschen(Guid freizeitId, Guid postenId, ISender sender, CancellationToken abbruch) =>
        await sender.Send(new EinkaufspostenLoeschenCommand(freizeitId, postenId), abbruch) switch
        {
            KuecheLoeschen.Geloescht => TypedResults.NoContent(),
            KuecheLoeschen.Abgeschlossen => KuecheProbleme.Abgeschlossen(),
            _ => TypedResults.NotFound(),
        };
}
