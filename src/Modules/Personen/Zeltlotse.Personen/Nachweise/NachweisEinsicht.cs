using Zeltlotse.Personen.Contracts.Events;
using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Personen.Nachweise;

/// <summary>Status einer <see cref="NachweisEinsicht"/>; gespeichert als Zahl, nur anhängen. Setzen nur die Methoden (AD-28).</summary>
internal enum EinsichtStatus : short
{
    Angefragt = 1,
    Freigegeben = 2,
    Abgeschlossen = 3,
}

/// <summary>Entscheidung zu einem Posten; gespeichert als Zahl, nur anhängen.</summary>
internal enum PostenErgebnisArt : short
{
    Offen = 0,
    Bestaetigt = 1,
    Abgelehnt = 2,
}

/// <summary>
/// Einmalige, befristete Einsicht einer Organisation in die Nachweise einer Person (FR-10, AD-9, AD-28). Sie entsteht
/// automatisch mit einer Freizeit-Rolle oder auf Anfrage von Leitung bzw. Org-Admin. Die Person gibt für
/// <see cref="Freigabedauer"/> frei; genau eine prüfende Person bestätigt oder lehnt je Nachweis ab. Die Bestätigung legt
/// <see cref="NachweisEingesehen"/> ab (Datensatz im Umschlag ist die Einsicht). Offen ist eine Einsicht bis zum Abschluss, auch
/// mit abgelaufener Freigabe (<see cref="IstOffen"/>); je (Person, Organisation) gibt es höchstens eine offene (partieller
/// eindeutiger Index).
/// </summary>
internal sealed class NachweisEinsicht : Aggregat, IOrganisationsEigen
{
    /// <summary>So lange sieht die prüfende Person die Dateien nach der Freigabe höchstens.</summary>
    public static readonly TimeSpan Freigabedauer = TimeSpan.FromDays(14);

    private readonly List<NachweisEinsichtPosten> _posten = [];

    private NachweisEinsicht()
    {
    }

    public Guid OrganisationId { get; private init; }

    public Guid PersonId { get; private init; }

    /// <summary>Die Freizeit, aus der die Anfrage kommt; bei einer Anfrage der Organisation <c>null</c>.</summary>
    public Guid? FreizeitId { get; private init; }

    /// <summary>Wer angefragt hat; <c>null</c> bei der automatischen Anfrage mit einer Freizeit-Rolle.</summary>
    public Guid? AngefragtVon { get; private init; }

    /// <summary>Die eine prüfende Person (AD-9); bei einer automatischen Anfrage bis zur Übernahme <c>null</c>.</summary>
    public Guid? PruefendePersonId { get; private set; }

    public EinsichtStatus Status { get; private set; }

    public DateTimeOffset AngefragtAm { get; private init; }

    public DateTimeOffset? FreigegebenAm { get; private set; }

    public DateTimeOffset? FreigegebenBis { get; private set; }

    public DateTimeOffset? AbgeschlossenAm { get; private set; }

    /// <summary>
    /// Nur bei der Neu-Anfrage nach einer Ablehnung: die abgelehnten Arten; nur sie kommen beim Freigeben in die Einsicht.
    /// Sonst <c>null</c>.
    /// </summary>
    public short[]? NurArten { get; private init; }

    /// <summary>Versionsprüfung (xmin).</summary>
    public uint Version { get; private set; }

    public IReadOnlyList<NachweisEinsichtPosten> Posten => _posten;

    public static NachweisEinsicht Anfragen(
        Guid id,
        Guid organisationId,
        Guid personId,
        Guid? freizeitId,
        Guid? angefragtVon,
        Guid? pruefendePersonId,
        DateTimeOffset jetzt,
        IReadOnlyCollection<NachweisArt>? nurArten = null) => new()
    {
        Id = id,
        OrganisationId = organisationId,
        PersonId = personId,
        FreizeitId = freizeitId,
        AngefragtVon = angefragtVon,
        PruefendePersonId = pruefendePersonId,
        AngefragtAm = jetzt,
        Status = EinsichtStatus.Angefragt,
        NurArten = nurArten is null ? null : [.. nurArten.Distinct().Order().Select(a => (short)a)],
    };

    /// <summary>Nicht abgeschlossen, auch mit abgelaufener Freigabe. Erst der Abschluss beendet eine Einsicht.</summary>
    public bool IstOffen => Status != EinsichtStatus.Abgeschlossen;

    /// <summary>Freigegeben und die Freigabe ist nicht abgelaufen; nur dann sieht die prüfende Person Dateien.</summary>
    public bool FreigabeGueltig(DateTimeOffset jetzt) => Status == EinsichtStatus.Freigegeben && FreigegebenBis >= jetzt;

    /// <summary>Freigegeben, aber die Freigabe ist abgelaufen; die Person kann dieselbe Einsicht erneut freigeben.</summary>
    public bool FreigabeAbgelaufen(DateTimeOffset jetzt) => Status == EinsichtStatus.Freigegeben && FreigegebenBis < jetzt;

    /// <summary>Angefragt oder mit abgelaufener Freigabe: Die Person darf (erneut) freigeben.</summary>
    public bool Freigebbar(DateTimeOffset jetzt) => Status == EinsichtStatus.Angefragt || FreigabeAbgelaufen(jetzt);

    /// <summary>
    /// Die Person gibt frei (auch nach abgelaufener Freigabe erneut): ein Posten je eigenem Nachweis mit Datei, dessen Art in
    /// dieser Einsicht noch nicht entschieden ist und, falls <see cref="NurArten"/> gesetzt ist, darin steht; offene Posten einer
    /// früheren Freigabe gelten nicht weiter. Ohne Posten bleibt alles, wie es ist (<c>false</c>). Nur wenn
    /// <see cref="Freigebbar"/>.
    /// </summary>
    public bool Freigeben(IReadOnlyCollection<(Guid NachweisId, NachweisArt Art)> nachweiseMitDatei, DateTimeOffset jetzt)
    {
        if (!Freigebbar(jetzt))
        {
            throw new InvalidOperationException("Freigeben geht nur bei einer angefragten Einsicht oder nach Ablauf der Freigabe.");
        }

        var entschieden = _posten.Where(p => p.Ergebnis != PostenErgebnisArt.Offen).Select(p => p.Art).ToHashSet();
        var neu = nachweiseMitDatei
            .Where(n => !entschieden.Contains(n.Art) && (NurArten is null || NurArten.Contains((short)n.Art)))
            .ToList();
        if (neu.Count == 0)
        {
            return false;
        }

        // Ein offener Posten derselben Datei bleibt bestehen (gleiche Kennung), alle anderen offenen fallen weg.
        _posten.RemoveAll(p => p.Ergebnis == PostenErgebnisArt.Offen && neu.All(n => n.NachweisId != p.NachweisId));
        foreach (var (nachweisId, art) in neu.Where(n => _posten.All(p => p.NachweisId != n.NachweisId)))
        {
            _posten.Add(NachweisEinsichtPosten.Anlegen(Id, nachweisId, art));
        }

        Status = EinsichtStatus.Freigegeben;
        FreigegebenAm = jetzt;
        FreigegebenBis = jetzt + Freigabedauer;
        return true;
    }

    /// <summary>
    /// Bindet die Einsicht an genau eine prüfende Person (AD-9). Den Weg (Rolle der Person in der Freizeit der Route oder
    /// Org-Route, nicht die Person selbst, gültige Freigabe) prüft der Command; hier gilt nur: Wer gebunden ist, bleibt gebunden.
    /// </summary>
    public void PruefungUebernehmen(Guid pruefendePersonId)
    {
        if (PruefendePersonId is { } gebunden && gebunden != pruefendePersonId)
        {
            throw new InvalidOperationException("Die Einsicht ist schon an eine andere prüfende Person gebunden.");
        }

        PruefendePersonId = pruefendePersonId;
    }

    /// <summary>Der offene Posten eines Nachweises, wenn die gebundene Person bei gültiger Freigabe entscheiden darf.</summary>
    public NachweisEinsichtPosten? OffenerPosten(Guid pruefendePersonId, Guid nachweisId, DateTimeOffset jetzt) =>
        PruefendePersonId == pruefendePersonId && FreigabeGueltig(jetzt)
            ? _posten.SingleOrDefault(p => p.NachweisId == nachweisId && p.Ergebnis == PostenErgebnisArt.Offen)
            : null;

    /// <summary>
    /// Bestätigt einen offenen Posten und legt <see cref="NachweisEingesehen"/> ab. Sind danach alle Posten entschieden, ist die
    /// Einsicht abgeschlossen.
    /// </summary>
    public void Bestaetigen(Guid nachweisId, DateOnly ausstellungsdatum, DateTimeOffset jetzt)
    {
        var posten = GueltigerPosten(nachweisId, jetzt);
        posten.Entscheiden(PostenErgebnisArt.Bestaetigt, jetzt);
        EreignisAblegen(new NachweisEingesehen(PersonId, nachweisId, posten.Art, ausstellungsdatum, jetzt, PruefendePersonId!.Value));
        AbschliessenWennEntschieden(jetzt);
    }

    /// <summary>Lehnt einen offenen Posten ab (den Hinweis speichert der Nachweis). Abschluss wie bei <see cref="Bestaetigen"/>.</summary>
    public void Ablehnen(Guid nachweisId, DateTimeOffset jetzt)
    {
        GueltigerPosten(nachweisId, jetzt).Entscheiden(PostenErgebnisArt.Abgelehnt, jetzt);
        AbschliessenWennEntschieden(jetzt);
    }

    /// <summary>Abgeschlossen mit mindestens einer Ablehnung: Die Einsicht wird neu angefordert (FR-10).</summary>
    public bool MitAblehnungAbgeschlossen =>
        Status == EinsichtStatus.Abgeschlossen && _posten.Any(p => p.Ergebnis == PostenErgebnisArt.Abgelehnt);

    /// <summary>
    /// Die neue offene Einsicht nach einer Ablehnung (FR-10 „neu angefordert“): gleiche Organisation, Freizeit und prüfende Person,
    /// angefragt von der prüfenden Person, nur für die abgelehnten Arten. Schon Bestätigtes wird nicht erneut gezeigt.
    /// </summary>
    public NachweisEinsicht NeuAnfordern(Guid id, DateTimeOffset jetzt)
    {
        if (!MitAblehnungAbgeschlossen)
        {
            throw new InvalidOperationException("Neu angefordert wird nur nach einem Abschluss mit Ablehnung.");
        }

        return Anfragen(
            id, OrganisationId, PersonId, FreizeitId, PruefendePersonId, PruefendePersonId, jetzt,
            [.. _posten.Where(p => p.Ergebnis == PostenErgebnisArt.Abgelehnt).Select(p => p.Art)]);
    }

    private NachweisEinsichtPosten GueltigerPosten(Guid nachweisId, DateTimeOffset jetzt) =>
        OffenerPosten(PruefendePersonId ?? Guid.Empty, nachweisId, jetzt)
        ?? throw new InvalidOperationException("Kein offener Posten bei gültiger Freigabe.");

    private void AbschliessenWennEntschieden(DateTimeOffset jetzt)
    {
        if (_posten.All(p => p.Ergebnis != PostenErgebnisArt.Offen))
        {
            Status = EinsichtStatus.Abgeschlossen;
            AbgeschlossenAm = jetzt;
        }
    }
}

/// <summary>Ein Nachweis in einer freigegebenen Einsicht mit der Entscheidung der prüfenden Person; Schlüssel (Einsicht, Nachweis).</summary>
internal sealed class NachweisEinsichtPosten
{
    private NachweisEinsichtPosten()
    {
    }

    public Guid EinsichtId { get; private init; }

    public Guid NachweisId { get; private init; }

    public NachweisArt Art { get; private init; }

    public PostenErgebnisArt Ergebnis { get; private set; }

    public DateTimeOffset? EntschiedenAm { get; private set; }

    public static NachweisEinsichtPosten Anlegen(Guid einsichtId, Guid nachweisId, NachweisArt art) => new()
    {
        EinsichtId = einsichtId,
        NachweisId = nachweisId,
        Art = art,
        Ergebnis = PostenErgebnisArt.Offen,
    };

    public void Entscheiden(PostenErgebnisArt ergebnis, DateTimeOffset jetzt)
    {
        if (Ergebnis != PostenErgebnisArt.Offen || ergebnis == PostenErgebnisArt.Offen)
        {
            throw new InvalidOperationException("Ein Posten wird genau einmal entschieden.");
        }

        Ergebnis = ergebnis;
        EntschiedenAm = jetzt;
    }
}
