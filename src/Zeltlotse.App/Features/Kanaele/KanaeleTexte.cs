using Zeltlotse.Api.Contracts.Freizeiten;
using Zeltlotse.Api.Contracts.Kanaele;
using Zeltlotse.Ui.Api;
using Zeltlotse.Ui.Komponenten;
using Zeltlotse.Ui.Texte;

namespace Zeltlotse.App.Features.Kanaele;

/// <summary>Story 6-1: Texte, Ziele und Namen der Kanal-Seite. Story 6-3: dazu Ansagen-Zettel und Besprechung.</summary>
public static class KanaeleTexte
{
    public const string AbgeschlossenText = "Diese Freizeit ist abgeschlossen. Die Kanäle sind nur noch lesbar.";

    public const string NichtGesendetText = "Nachricht nicht gesendet. Bitte noch einmal versuchen.";

    public const string GeloeschtePersonText = "Gelöschte Person";

    public const string OhneNamenText = "Ohne Namen";

    /// <summary>Push ist auf diesem Gerät an (für diese Freizeit gespeichert).</summary>
    public const string PushAnText = "Benachrichtigungen sind auf diesem Gerät eingeschaltet.";

    /// <summary>Push ist möglich, aber aus.</summary>
    public const string PushAusText = "Neue Nachrichten in „Team“ und „Alle“ kommen dann als Benachrichtigung auf dieses Gerät.";

    /// <summary>EXPERIENCE.md, „Push abgelehnt“.</summary>
    public const string PushAbgelehntText = "Benachrichtigungen sind auf diesem Gerät ausgeschaltet.";

    /// <summary>EXPERIENCE.md, „Push nicht möglich“ (iPhone ohne Home-Bildschirm).</summary>
    public const string PushHomeBildschirmText = "Auf dem iPhone kommen Benachrichtigungen erst, wenn Zeltlotse auf dem Home-Bildschirm liegt.";

    /// <summary>Der Browser kennt Web-Push nicht.</summary>
    public const string PushNichtMoeglichText = "Dieser Browser kann keine Benachrichtigungen empfangen.";

    /// <summary>Unter jedem Hinweis ohne Push.</summary>
    public const string KanaeleBleibenText = "Die Kanäle bleiben lesbar und beschreibbar.";

    public const string PushFehlerText = "Benachrichtigungen ließen sich nicht einschalten. Bitte noch einmal versuchen.";

    /// <summary>Die Kanal-Seite: „Team“ ohne Zusatz, „Alle“ mit <c>?kanal=alle</c>.</summary>
    public static string Ziel(Guid freizeitId, Kanal kanal = Kanal.Team) =>
        $"freizeiten/{freizeitId}/kanaele" + (kanal == Kanal.Alle ? "?kanal=alle" : "");

    /// <summary>Story 6-3: Die Seite einer Notiz-Liste (<c>…/kanaele/ansagen</c> oder <c>…/kanaele/besprechung</c>).</summary>
    public static string NotizenZiel(Guid freizeitId, NotizArt art) => $"freizeiten/{freizeitId}/kanaele/{KanalFelder.NotizArtInRoute(art)}";

    /// <summary>Story 6-3: Überschrift und Reiter einer Notiz-Liste.</summary>
    public static string NotizenTitel(NotizArt art) => art == NotizArt.Besprechung ? "Besprechung" : "Ansagen-Zettel";

    /// <summary>Story 6-3: Leer-Zustand einer Notiz-Liste.</summary>
    public static string KeineNotizenText(NotizArt art) => art == NotizArt.Besprechung
        ? "Noch keine Punkte für die Mitarbeiterbesprechung."
        : "Noch keine Ansagen für das nächste Essen.";

    /// <summary>Story 6-3: Unter „Offen“, wenn alle Punkte abgehakt sind.</summary>
    public const string AllesAbgehaktText = "Alles abgehakt.";

    /// <summary>Story 6-3: Erklärung über der Liste.</summary>
    public const string NotizenErklaerungText = "Ein Tipp auf das Kästchen hakt einen Punkt ab. Ein weiterer Tipp holt ihn zurück.";

    public const string NotizenAbgeschlossenText = "Diese Freizeit ist abgeschlossen. Ansagen-Zettel und Besprechung sind nur noch lesbar.";

    public const string NichtGespeichertText = "Nicht gespeichert. Bitte noch einmal versuchen.";

    /// <summary>Die zwei Segmente.</summary>
    public static IReadOnlyList<AuswahlOption> KanalOptionen { get; } = [new("team", "Team"), new("alle", "Alle")];

    /// <summary>„An 42 Personen senden?“ (eine Person: „An 1 Person senden?“).</summary>
    public static string BestaetigungSatz(int personen) => personen == 1 ? "An 1 Person senden?" : $"An {personen} Personen senden?";

    /// <summary>
    /// Je Person der Leitung (aus der Freizeit) und des Teams (bestätigte Mitarbeitende der Personenliste) Vor- und Nachname (AD-2: Kanäle kennt keine Namen).
    /// Fehlt eine Quelle, fehlen nur ihre Namen.
    /// </summary>
    public static IReadOnlyDictionary<Guid, (string? Vorname, string? Nachname)> Personen(FreizeitAntwort? freizeit, IEnumerable<PersonenlisteZeile>? personen)
    {
        var namen = new Dictionary<Guid, (string?, string?)>();
        foreach (var person in (personen ?? []).Where(p => p.Art == AnmeldungArt.Mitarbeitende && p.Status == AnmeldungStatus.Bestaetigt))
        {
            namen[person.PersonId] = (person.Vorname, person.Nachname);
        }

        foreach (var leitung in freizeit?.Leitung ?? [])
        {
            namen[leitung.PersonId] = (leitung.Vorname, leitung.Nachname);
        }

        return namen;
    }

    /// <summary>Eine Nachricht für die Liste: Initialen und Name der Person, die geschrieben hat; gelöscht „Gelöschte Person“, unbekannt „Ohne Namen“.</summary>
    public static KanalNachrichtEintrag Eintrag(KanalNachricht nachricht, IReadOnlyDictionary<Guid, (string? Vorname, string? Nachname)> personen)
    {
        if (nachricht.AutorPersonId == KanalFelder.GeloeschtePerson)
        {
            return new KanalNachrichtEintrag("?", GeloeschtePersonText, nachricht.GesendetAm, nachricht.Text);
        }

        return personen.TryGetValue(nachricht.AutorPersonId, out var name)
            ? new KanalNachrichtEintrag(Anzeige.Initialen(name.Vorname, name.Nachname), Anzeige.Name(name.Vorname, name.Nachname), nachricht.GesendetAm, nachricht.Text)
            : new KanalNachrichtEintrag("?", OhneNamenText, nachricht.GesendetAm, nachricht.Text);
    }

    /// <summary>Lädt Freizeit und Personenliste für die Namen; scheitert ein Teil, fehlen nur dessen Namen.</summary>
    public static async Task<IReadOnlyDictionary<Guid, (string? Vorname, string? Nachname)>> PersonenLadenAsync(FreizeitenApiClient freizeiten, Guid freizeitId)
    {
        var freizeit = Leise(freizeiten.LadenAsync(freizeitId));
        var personen = Leise(freizeiten.PersonenlisteLadenAsync(freizeitId));
        await Task.WhenAll(freizeit, personen);
        return Personen(freizeit.Result, personen.Result);
    }

    private static async Task<T?> Leise<T>(Task<T?> abruf)
        where T : class
    {
        try
        {
            return await abruf;
        }
        catch (Exception fehler) when (Zeltlotse.App.Features.Konten.AnmeldungAbschluss.IstAktionsfehler(fehler))
        {
            return null;
        }
    }
}
