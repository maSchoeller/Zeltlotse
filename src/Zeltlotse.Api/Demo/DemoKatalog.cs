using Zeltlotse.Api.Contracts.Freizeiten;
using NachweisArt = Zeltlotse.Personen.Contracts.Events.NachweisArt;

namespace Zeltlotse.Api.Demo;

/// <summary>
/// Erfundene Stammdaten für Screenshots und Anwender-Doku (NFR-20, Konvention „Anwender-Doku“): eine Organisation,
/// eine Freizeit und Personen mit Rolle. Alle Mail-Adressen enden auf <c>@example.org</c>; Telefonnummern sind erfunden und beginnen mit
/// <c>0123</c> (Kontakt für Absagen, Story 2-3; Notfallkontakt im Freizeitpass, Story 2-4),
/// keine echten Namen von Organisationen. Die Fach-Stories spielen diese Daten über ihre Commands ein (<see cref="DemoDaten"/>).
/// </summary>
internal static class DemoKatalog
{
    public static DemoOrganisation Organisation { get; } = new("Jugendwerk Musterstadt");

    /// <summary>Offene Einladung als Org-Überblick (von Andrea), ohne Konto dahinter.</summary>
    public const string UeberblickEinladung = "vorstand@example.org";

    public static DemoFreizeit Freizeit { get; } = new(
        "Zeltlager Sommer 2027",
        "Zeltplatz am Mühlbach, Musterhausen",
        new DateOnly(2027, 8, 2),
        new DateOnly(2027, 8, 13),
        "Zwölf Tage Zeltlager am Mühlbach: Geländespiel, Lagerfeuer, Bachwanderung und gemeinsames Essen im großen Zelt.\nFür Kinder und Jugendliche von 9 bis 14 Jahren.",
        40,
        "Die Lagerregeln und die Anweisungen der Mitarbeitenden gelten für alle Teilnehmenden.\nDer Teilnahmebeitrag wird nach der Zusage fällig. Wer krank wird, sagt bei der Leitung ab.");

    /// <summary>
    /// Story 3-7: Module der Demo-Freizeit, die Miriam einschaltet (je nur, wenn noch aus): Verpflegung und Finanzen an; Story 6-4: dazu der
    /// Teilnehmer-Zugang mit QR-Link. Die zweite Freizeit (Kinderbibeltage) nutzt kein Modul.
    /// </summary>
    public static IReadOnlyList<ModulFeld> Module { get; } = [ModulFeld.Verpflegung, ModulFeld.Finanzen, ModulFeld.TeilnehmerZugang];

    /// <summary>
    /// Story 4-1: Rezepte der Küche in der Demo-Freizeit (Modul Verpflegung), je für 4 Personen, mit Zubereitung und Zutaten (Menge als Text wie im
    /// Formular, Allergene als Komma-Liste der Schlüssel). Je Rezept nur, wenn es den Namen noch nicht gibt, je Zutat nur, wenn es die Bezeichnung
    /// im Rezept noch nicht gibt.
    /// </summary>
    public static IReadOnlyList<DemoRezept> Rezepte { get; } =
    [
        new(
            "Chili sin Carne",
            4,
            "Zwiebeln und Paprika würfeln und in Öl andünsten. Tomaten, Bohnen, Mais und Gemüsebrühe dazugeben, mit Chili würzen und 30 Minuten köcheln lassen.",
            [
                new("Kidneybohnen", "500", "g", null),
                new("Mais", "300", "g", null),
                new("Paprika", "2", "Stück", null),
                new("Zwiebeln", "2", "Stück", null),
                new("Gehackte Tomaten", "800", "g", null),
                new("Gemüsebrühe", "0,5", "l", "sellerie"),
                new("Chilipulver", null, null, null),
            ]),
        new(
            "Pfannkuchen",
            4,
            "Mehl, Milch, Eier und eine Prise Salz zu einem glatten Teig rühren und 10 Minuten quellen lassen. In der Pfanne dünn ausbacken.",
            [
                new("Mehl", "400", "g", "gluten"),
                new("Milch", "0,75", "l", "milch"),
                new("Eier", "4", "Stück", "eier"),
                new("Salz", null, null, null),
            ]),
    ];

    /// <summary>
    /// Story 4-2: Gerichte im Essensplan der Demo-Freizeit (Tag, Titel der Mahlzeit, Rezept); je nur, wenn das Rezept an dieser Mahlzeit noch nicht
    /// vorgemerkt ist.
    /// </summary>
    public static IReadOnlyList<(DateOnly Tag, string Mahlzeit, string Rezept)> Gerichte { get; } =
    [
        (new DateOnly(2027, 8, 3), "Frühstück", "Pfannkuchen"),
        (new DateOnly(2027, 8, 3), "Mittagessen", "Chili sin Carne"),
    ];

    /// <summary>Story 4-2: Hand-Mengen der Einkaufsliste (Tag, Mahlzeit, Rezept, Zutat, Menge als Text); je nur, wenn es dort noch keine gibt.</summary>
    public static IReadOnlyList<(DateOnly Tag, string Mahlzeit, string Rezept, string Zutat, string Menge)> HandMengen { get; } =
    [
        (new DateOnly(2027, 8, 3), "Frühstück", "Pfannkuchen", "Mehl", "500"),
    ];

    /// <summary>Story 4-2: Hand-Posten der Einkaufsliste; je nur, wenn es die Bezeichnung noch nicht gibt.</summary>
    public static IReadOnlyList<(string Bezeichnung, string? Menge, string? Einheit, DateOnly? Tag)> Einkaufsposten { get; } =
    [
        ("Äpfel", "3", "kg", new DateOnly(2027, 8, 3)),
    ];

    /// <summary>Story 4-3: Tage, die die Küche in der Demo ganz zum Einkaufen freigibt.</summary>
    public static IReadOnlyList<DateOnly> EinkaufTage { get; } = [new DateOnly(2027, 8, 3)];

    /// <summary>Story 4-3: Hand-Posten, deren Freigabe die Küche danach wieder zurücknimmt (Bezeichnung).</summary>
    public static IReadOnlyList<string> NichtFreigegeben { get; } = ["Äpfel"];

    /// <summary>
    /// Story 5-1: Kalkulation der Demo-Freizeit (Modul Finanzen): 40 Teilnehmende und 10 Mitarbeitende, Beträge in allen sechs Kategorien (Fixkosten und
    /// Kosten pro Person und Tag als Text wie im Formular; Verpflegung 7 € Tagessatz) und zwei Einnahmen. Bei 12 Tagen ergibt das 14.200 € Kosten,
    /// 2.200 € Einnahmen und 300,00 € Teilnehmerbeitrag. Planzahlen und Beträge nur, solange sie 0 sind, Einnahmen nur, wenn es die Bezeichnung noch nicht gibt.
    /// </summary>
    public static (int Teilnehmende, int Mitarbeitende) Planzahlen { get; } = (40, 10);

    /// <inheritdoc cref="Planzahlen"/>
    public static IReadOnlyList<(Zeltlotse.Api.Contracts.Finanzen.Kostenkategorie Kategorie, string Fixkosten, string ProPersonUndTag)> Kostenkategorien { get; } =
    [
        (Zeltlotse.Api.Contracts.Finanzen.Kostenkategorie.Unterkunft, "1500", "8"),
        (Zeltlotse.Api.Contracts.Finanzen.Kostenkategorie.Verpflegung, "0", "7"),
        (Zeltlotse.Api.Contracts.Finanzen.Kostenkategorie.Fahrt, "1800", "0"),
        (Zeltlotse.Api.Contracts.Finanzen.Kostenkategorie.ProgrammUndMaterial, "600", "1"),
        (Zeltlotse.Api.Contracts.Finanzen.Kostenkategorie.Mitarbeitende, "500", "0"),
        (Zeltlotse.Api.Contracts.Finanzen.Kostenkategorie.Sonstiges, "200", "0"),
    ];

    /// <inheritdoc cref="Planzahlen"/>
    public static IReadOnlyList<(string Bezeichnung, string Betrag)> Einnahmen { get; } =
    [
        ("Zuschuss Landesjugendplan", "1200"),
        ("Rabatt der Organisation", "1000"),
    ];

    /// <summary>
    /// Story 5-2: Belege der Demo-Freizeit (Bezeichnung, Kategorie, Betrag als Text, wer bezahlt hat, ob erstattet, ob mit Foto). Gegen die Kalkulation ist
    /// Verpflegung mit 3.900 € von 4.200 € „Knapp“, Fahrt mit 1.950 € von 1.800 € „Überschritten“, der Rest „Im Plan“. Lea hat zwei offene Belege
    /// (45,80 €), Jonas und Miriam sind ganz erstattet. Je Bezeichnung nur, wenn es sie noch nicht gibt; Vermerk und Foto nur beim Anlegen.
    /// </summary>
    public static IReadOnlyList<(string Bezeichnung, Zeltlotse.Api.Contracts.Finanzen.Kostenkategorie Kategorie, string Betrag, string Mail, bool Erstattet, bool Foto)> Belege { get; } =
    [
        ("Anzahlung Zeltplatz", Zeltlotse.Api.Contracts.Finanzen.Kostenkategorie.Unterkunft, "1500", "miriam.zeltner@example.org", true, false),
        ("Busfahrt Hin- und Rückweg", Zeltlotse.Api.Contracts.Finanzen.Kostenkategorie.Fahrt, "1950", "miriam.zeltner@example.org", true, false),
        ("Großeinkauf Lebensmittel", Zeltlotse.Api.Contracts.Finanzen.Kostenkategorie.Verpflegung, "3854,20", "jonas.huettner@example.org", true, false),
        ("Bastelmaterial", Zeltlotse.Api.Contracts.Finanzen.Kostenkategorie.ProgrammUndMaterial, "180", "jonas.huettner@example.org", true, false),
        ("Getränke", Zeltlotse.Api.Contracts.Finanzen.Kostenkategorie.Verpflegung, "22,40", "lea.feuerstein@example.org", false, false),
        ("Brötchen und Obst", Zeltlotse.Api.Contracts.Finanzen.Kostenkategorie.Verpflegung, "23,40", "lea.feuerstein@example.org", false, true),
    ];

    /// <summary>
    /// Story 5-3: Beiträge, die als bezahlt abgehakt sind (Mail der Eltern, Vorname des Kindes, Mail der Person, die abhakt): Lea hakt Mia ab. Mia ist die
    /// einzige bestätigte Teilnehmende, also „1 von 1 bezahlt“. Nur, solange es noch keinen Vermerk gibt.
    /// </summary>
    public static IReadOnlyList<(string ElternMail, string Kind, string AbgehaktVonMail)> BeitraegeBezahlt { get; } =
    [
        ("daniel.probst@example.org", "Mia", "lea.feuerstein@example.org"),
    ];

    /// <summary>
    /// Story 6-1: Nachrichten in den Kanälen der Demo-Freizeit, in dieser Reihenfolge (Kanal, Mail der Person, die schreibt, Text). Jonas und Miriam sind
    /// Leitung, Lea Team. Nur, solange die Freizeit noch keine Nachricht hat.
    /// </summary>
    public static IReadOnlyList<(Zeltlotse.Api.Contracts.Kanaele.Kanal Kanal, string Mail, string Text)> KanalNachrichten { get; } =
    [
        (Zeltlotse.Api.Contracts.Kanaele.Kanal.Team, "jonas.huettner@example.org", "Feuerholz liegt hinter dem Küchenzelt. Wer übernimmt heute die Nachtwache?"),
        (Zeltlotse.Api.Contracts.Kanaele.Kanal.Team, "lea.feuerstein@example.org", "Ich übernehme bis 1 Uhr, danach Miriam."),
        (Zeltlotse.Api.Contracts.Kanaele.Kanal.Team, "miriam.zeltner@example.org", "Passt. Morgen um 7:30 kurze Teambesprechung am Lagerfeuerplatz."),
        (Zeltlotse.Api.Contracts.Kanaele.Kanal.Alle, "jonas.huettner@example.org", "Mittagessen heute um 13:30 statt 13:00."),
        (Zeltlotse.Api.Contracts.Kanaele.Kanal.Alle, "miriam.zeltner@example.org", "Wandertag morgen: feste Schuhe, Regenjacke und eine volle Trinkflasche einpacken."),
    ];

    /// <summary>
    /// Story 6-3: Punkte auf Ansagen-Zettel und Besprechungsliste der Demo-Freizeit, in dieser Reihenfolge (Liste, Text, abgehakt). Zwei offene Ansagen und drei
    /// Besprechungspunkte, davon einer abgehakt. Nur, solange die Freizeit noch keinen Punkt hat.
    /// </summary>
    public static IReadOnlyList<(Zeltlotse.Api.Contracts.Kanaele.NotizArt Art, string Text, bool Abgehakt)> KanalNotizen { get; } =
    [
        (Zeltlotse.Api.Contracts.Kanaele.NotizArt.Ansage, "Taschenlampen für die Nachtwanderung mitbringen.", false),
        (Zeltlotse.Api.Contracts.Kanaele.NotizArt.Ansage, "Nach dem Abendessen Zeltkontrolle um 21 Uhr.", false),
        (Zeltlotse.Api.Contracts.Kanaele.NotizArt.Besprechung, "Programm für den Regentag festlegen", true),
        (Zeltlotse.Api.Contracts.Kanaele.NotizArt.Besprechung, "Nachtwache für Donnerstag einteilen", false),
        (Zeltlotse.Api.Contracts.Kanaele.NotizArt.Besprechung, "Spülplan für die zweite Woche", false),
    ];

    /// <summary>Story 4-4: Mahlzeiten, die die Küche als Restetag markiert (Tag, Titel der Mahlzeit).</summary>
    public static IReadOnlyList<(DateOnly Tag, string Mahlzeit)> Restetage { get; } = [(new DateOnly(2027, 8, 3), "Abendessen")];

    /// <summary>Story 4-3: Posten, die beim Einkaufen schon abgehakt sind (Tag, Mahlzeit, Rezept, Zutat); je nur, wenn sie noch nicht gekauft sind.</summary>
    public static IReadOnlyList<(DateOnly Tag, string Mahlzeit, string Rezept, string Zutat)> Gekauft { get; } =
    [
        (new DateOnly(2027, 8, 3), "Frühstück", "Pfannkuchen", "Mehl"),
        (new DateOnly(2027, 8, 3), "Frühstück", "Pfannkuchen", "Milch"),
    ];

    /// <summary>
    /// Story 2-2 und 2-3: Anmeldungen der Demo-Freizeit mit Konto, in dieser Reihenfolge. <c>Kind</c> leer heißt: die Person meldet sich
    /// selbst über den Anmeldelink für Mitarbeitende an. Miriam bestätigt die mit <c>Bestaetigt</c>; die anderen bleiben „eingegangen“.
    /// So zeigt die Liste der Leitung alle Status und der Balken „1 von 40“ (Mia; Mitarbeitende zählen nicht).
    /// </summary>
    public static IReadOnlyList<DemoAnmeldung> Anmeldungen { get; } =
    [
        new("katrin.muster@example.org", "Emma", Bestaetigt: false),
        new("daniel.probst@example.org", "Mia", Bestaetigt: true),
        new("lea.feuerstein@example.org", Kind: null, Bestaetigt: true),
        new("tobias.wanderer@example.org", Kind: null, Bestaetigt: false),
    ];

    /// <summary>
    /// Story 2-4: Fragen des Freizeitpasses der Demo-Freizeit, die Miriam auswählt (je Zielgruppe nur, wenn noch keine gewählt sind).
    /// </summary>
    public static (string Teilnehmende, string Mitarbeitende) Fragen { get; } =
        ("allergien,medikamente,notfallkontakte,ernaehrung,krankheiten,tetanus,schwimmen,zecken-entfernen", "allergien,medikamente,notfallkontakte,ernaehrung");

    /// <summary>Story 2-4: Katrins Antworten für Emma im Freizeitpass (je Frage nur, wenn es noch keine Antwort gibt).</summary>
    public static IReadOnlyList<(string Kennung, string Wert)> EmmasFreizeitpass { get; } =
    [
        ("allergien", "milch"),
        ("notfallkontakte", "Katrin Muster (Mutter), 0123 456780"),
        ("schwimmen", "ja"),
    ];

    /// <summary>
    /// Story 2-5: Daniels Antworten für Mia im Freizeitpass (je Frage nur, wenn es noch keine Antwort gibt); Story 2-11: dazu die
    /// Medikamente für den Notfallordner; Story 4-4: dazu „vegetarisch“ für die Sonderkost der Küchen-Tagesansicht.
    /// </summary>
    public static IReadOnlyList<(string Kennung, string Wert)> MiasFreizeitpass { get; } =
    [
        ("allergien", "gluten"),
        ("notfallkontakte", "Daniel Probst (Vater), 0123 456781"),
        ("schwimmen", "nein"),
        ("medikamente", "Asthmaspray bei Bedarf, liegt im Kulturbeutel"),
        ("ernaehrung", "vegetarisch"),
    ];

    /// <summary>Story 2-5: Leas Team-Notiz zu Mia (nur, wenn es an Mias Anmeldung noch keine Notiz gibt).</summary>
    public const string TeamNotizZuMia = "Hatte letztes Jahr am ersten Abend Heimweh; ein Gespräch am Lagerfeuer hat geholfen.";

    /// <summary>
    /// Story 2-7: Org-Dokumente der Demo-Organisation (Andrea lädt sie hoch, je nur, wenn es den Titel noch nicht gibt), als Dateiname;
    /// der Titel ist der Name ohne Endung. Die Hausordnung bindet die Freizeit ein, der Leitfaden bleibt einbindbar.
    /// </summary>
    public static IReadOnlyList<string> OrgDokumente { get; } = ["Hausordnung Zeltplatz.pdf", "Leitfaden Aufsichtspflicht.pdf"];

    /// <summary>Story 2-7: das Org-Dokument, das die Demo-Freizeit mit Sichtbarkeit „Eltern und Teilnehmende“ einbindet.</summary>
    public const string EingebundenesOrgDokument = "Hausordnung Zeltplatz";

    /// <summary>
    /// Story 2-7: Dokumente der Demo-Freizeit (Dateiname und Sichtbarkeit), je nur, wenn es den Titel noch nicht gibt. Die Suche „pack“
    /// trifft in der Doku nur die Packliste.
    /// </summary>
    public static IReadOnlyList<(string Dateiname, DokumentSichtbarkeit Sichtbarkeit)> Dokumente { get; } =
    [
        ("Infobrief Zeltlager Sommer 2027.pdf", DokumentSichtbarkeit.ElternUndTeilnehmende),
        ("Packliste.pdf", DokumentSichtbarkeit.ElternUndTeilnehmende),
        ("Notfallnummern.pdf", DokumentSichtbarkeit.ElternUndTeilnehmende),
        ("Anfahrt zum Zeltplatz.pdf", DokumentSichtbarkeit.Oeffentlich),
        ("Programmideen.pdf", DokumentSichtbarkeit.Team),
    ];

    /// <summary>Story 2-3: Kontakt der Leitung für Absagen auf der Demo-Freizeit (erfunden, Mail-Adresse unter example.org).</summary>
    public static (string Mail, string Telefon) KontaktFuerAbsagen { get; } = ("zeltlager@example.org", "0123 456789");

    /// <summary>
    /// Story 2-8: Sicherheitskette der Demo-Freizeit (je Feld nur, wenn es leer ist); Telefone und Ansprechperson sind erfunden. Die Schritte
    /// stehen im Feld „Was jetzt zu tun ist“ je in einer eigenen Zeile.
    /// </summary>
    public static (IReadOnlyList<string> WasTun, string LeitungTelefon, string HeimatName, string HeimatTelefon) Sicherheitskette { get; } =
    (
        [
            "Ruhe bewahren und bei der verletzten Person bleiben.",
            "Bei Lebensgefahr sofort den Notruf 112 wählen.",
            "Leitung anrufen und Bescheid geben.",
            "Erste Hilfe leisten.",
        ],
        "0123 456782",
        "Petra Heimann (Gemeindebüro)",
        "0123 456783");

    /// <summary>
    /// Story 2-9: Titel des Org-Dokuments, das die Demo-Freizeit einbindet (Sichtbarkeit Team) und als Schutzkonzept wählt. Jonas hat es als
    /// gelesen bestätigt, Miriam und Lea noch nicht.
    /// </summary>
    public const string Schutzkonzept = "Schutzkonzept Jugendwerk Musterstadt";

    /// <summary>
    /// Story 2-10: drei Einträge im Vorfall-Protokoll zu Mia Probst in der Demo-Freizeit, in dieser Reihenfolge angelegt (feste Kennungen,
    /// damit das Einspielen idempotent bleibt). Archiviert ist nichts.
    /// </summary>
    public static IReadOnlyList<(Guid Id, string VerfasserMail, Zeltlotse.Api.Contracts.Sicherheit.VorfallArt Art, string Text)> Vorfaelle { get; } =
    [
        (Guid.Parse("0199e8a0-0000-7000-8000-00000000f001"), "lea.feuerstein@example.org", Zeltlotse.Api.Contracts.Sicherheit.VorfallArt.Geschehen,
            "Beim Geländespiel am linken Knie geschnitten. Wunde gereinigt und verbunden."),
        (Guid.Parse("0199e8a0-0000-7000-8000-00000000f002"), "lea.feuerstein@example.org", Zeltlotse.Api.Contracts.Sicherheit.VorfallArt.Entnahme,
            "2 Pflaster, 1 sterile Kompresse"),
        (Guid.Parse("0199e8a0-0000-7000-8000-00000000f003"), "jonas.huettner@example.org", Zeltlotse.Api.Contracts.Sicherheit.VorfallArt.Medikament,
            "Kühlpack, kein Medikament"),
    ];

    /// <summary>
    /// Story 3-1: Programmpunkte der Demo-Freizeit neben den festen Mahlzeiten (je nur, wenn es den Titel in der Freizeit noch nicht gibt), mit der
    /// Person, die ihn übernommen hat (Mail-Adresse) oder ohne. „Ich übernehme“ führt die Person selbst aus. Story 6-2: Mit „verschoben auf“ wird der
    /// Punkt nach dem Anlegen über den Autosave verschoben und nicht mitgeteilt („Geändert, noch nicht mitgeteilt“).
    /// </summary>
    public static IReadOnlyList<(string Titel, DateOnly Tag, TimeOnly Beginn, string? UebernommenVon, TimeOnly? VerschobenAuf)> Programmpunkte { get; } =
    [
        ("Anreise und Zeltaufbau", new DateOnly(2027, 8, 2), new TimeOnly(15, 0), null, null),
        ("Lagerfeuer", new DateOnly(2027, 8, 2), new TimeOnly(20, 0), "lea.feuerstein@example.org", null),
        ("Geländespiel", new DateOnly(2027, 8, 3), new TimeOnly(14, 0), "jonas.huettner@example.org", new TimeOnly(14, 30)),
    ];

    /// <summary>
    /// Story 3-2: Material an den Programmpunkten der Demo-Freizeit (je nur, wenn es die Bezeichnung am Programmpunkt noch nicht gibt), mit Menge,
    /// Status und der Person, die es mitbringt (Mail-Adresse) oder ohne. „Ich bringe das mit“ führt die Person selbst aus.
    /// </summary>
    public static IReadOnlyList<(string Programmpunkt, string Bezeichnung, string Menge, Zeltlotse.Api.Contracts.Programm.MaterialStatus Status, string? BringtMit)> Material { get; } =
    [
        ("Geländespiel", "Fahnen", "8 Stück", Zeltlotse.Api.Contracts.Programm.MaterialStatus.Vorhanden, null),
        ("Geländespiel", "Seile", "4 × 10 m", Zeltlotse.Api.Contracts.Programm.MaterialStatus.Gebraucht, null),
        ("Geländespiel", "Taschenlampen", "10 Stück", Zeltlotse.Api.Contracts.Programm.MaterialStatus.Gebraucht, "miriam.zeltner@example.org"),
        ("Lagerfeuer", "Feuerholz", "2 Kisten", Zeltlotse.Api.Contracts.Programm.MaterialStatus.Besorgt, null),
        ("Lagerfeuer", "Stockbrot-Teig", "3 kg", Zeltlotse.Api.Contracts.Programm.MaterialStatus.Eingepackt, null),
    ];

    /// <summary>
    /// Story 3-3: zweite Freizeit der Demo-Organisation, damit sich Reservierungen überschneiden können; ohne Anmeldungen, Leitung ist die Person
    /// mit <see cref="DemoRolle.LeitungZweiteFreizeit"/>. Jonas und Miriam bleiben bei genau einer Freizeit.
    /// </summary>
    public static (string Name, DateOnly Beginn, DateOnly Ende) ZweiteFreizeit { get; } = ("Kinderbibeltage 2027", new DateOnly(2027, 8, 10), new DateOnly(2027, 8, 12));

    /// <summary>Story 3-3: Artikel im Inventar der Demo-Organisation (je nur, wenn es die Bezeichnung noch nicht gibt).</summary>
    public static IReadOnlyList<(string Bezeichnung, string Menge)> Inventar { get; } =
    [
        ("Fahnen", "8 Stück"),
        ("Großzelt", "1 Stück"),
        ("Kanus", "6 Stück"),
        ("Gaskocher", "2 Stück"),
    ];

    /// <summary>
    /// Story 3-3: Reservierungen (je nur, wenn die Freizeit den Artikel noch nicht reserviert hat): das Zeltlager reserviert die Fahnen für das
    /// Material „Fahnen“ am Geländespiel und das Großzelt; die Kinderbibeltage reservieren das Großzelt zur gleichen Zeit (Konflikt).
    /// </summary>
    public static IReadOnlyList<(string Freizeit, string Artikel, DateOnly Von, DateOnly Bis, (string Programmpunkt, string Bezeichnung)? Material)> Reservierungen { get; } =
    [
        ("Zeltlager Sommer 2027", "Fahnen", new DateOnly(2027, 8, 2), new DateOnly(2027, 8, 13), ("Geländespiel", "Fahnen")),
        ("Zeltlager Sommer 2027", "Großzelt", new DateOnly(2027, 8, 2), new DateOnly(2027, 8, 13), null),
        ("Kinderbibeltage 2027", "Großzelt", new DateOnly(2027, 8, 10), new DateOnly(2027, 8, 12), null),
    ];

    /// <summary>
    /// Story 3-4: Einträge auf der Schadens- und Verlustliste der Demo-Freizeit (je nur, wenn es die Bezeichnung dort noch nicht gibt), angelegt
    /// wie von der Leitung (Jonas). Den dritten Eintrag „Erste-Hilfe-Kasten“ legt das Ereignis aus Leas Entnahme im Vorfall-Protokoll an.
    /// </summary>
    public static IReadOnlyList<(Zeltlotse.Api.Contracts.Inventar.SchadenArt Art, string Bezeichnung)> Schaeden { get; } =
    [
        (Zeltlotse.Api.Contracts.Inventar.SchadenArt.Schaden, "Großzelt: Stange verbogen"),
        (Zeltlotse.Api.Contracts.Inventar.SchadenArt.Verlust, "Taschenlampe"),
    ];

    /// <summary>Story 3-5: Aufgaben-Vorlagen der Demo-Organisation (je nur, wenn es den Titel dort noch nicht gibt), angelegt wie von Andrea.</summary>
    public static IReadOnlyList<(string Titel, Zeltlotse.Api.Contracts.Programm.AufgabenPhase Phase)> AufgabenVorlagen { get; } =
    [
        ("Zeltplatz-Vertrag prüfen", Zeltlotse.Api.Contracts.Programm.AufgabenPhase.Vorbereitung),
        ("Erste-Hilfe-Kasten auffüllen", Zeltlotse.Api.Contracts.Programm.AufgabenPhase.Vorbereitung),
        ("Zeltplatz besenrein übergeben", Zeltlotse.Api.Contracts.Programm.AufgabenPhase.Nachbereitung),
    ];

    /// <summary>
    /// Story 3-5: eigene Aufgaben der Demo-Freizeit neben den übernommenen Vorlagen (je nur, wenn es den Titel dort noch nicht gibt), optional am
    /// Programmpunkt (Titel) und mit der Person, die sie übernommen hat (Mail-Adresse).
    /// </summary>
    public static IReadOnlyList<(string Titel, Zeltlotse.Api.Contracts.Programm.AufgabenPhase Phase, string? Programmpunkt, string? UebernommenVon)> Aufgaben { get; } =
    [
        ("Andacht am Lagerfeuer vorbereiten", Zeltlotse.Api.Contracts.Programm.AufgabenPhase.Waehrend, "Lagerfeuer", "lea.feuerstein@example.org"),
        ("Fotos sammeln", Zeltlotse.Api.Contracts.Programm.AufgabenPhase.Nachbereitung, null, null),
    ];

    /// <summary>
    /// Story 3-5: Wer welche Aufgabe aus den Vorlagen übernimmt (Mail-Adresse, „Ich übernehme“ führt die Person selbst aus) und ob sie schon
    /// abgehakt ist: Lea übernimmt den Erste-Hilfe-Kasten, Jonas den Zeltplatz-Vertrag und hakt ihn ab.
    /// </summary>
    public static IReadOnlyList<(string Titel, string Person, bool Erledigt)> AufgabenUebernahmen { get; } =
    [
        ("Erste-Hilfe-Kasten auffüllen", "lea.feuerstein@example.org", false),
        ("Zeltplatz-Vertrag prüfen", "jonas.huettner@example.org", true),
    ];

    /// <summary>Story 3-6: Programmpunkte des Zeltlagers (Titel), die das Team in der Programmbibliothek der Organisation ablegt.</summary>
    public static IReadOnlyList<string> Bibliothek { get; } = ["Lagerfeuer", "Geländespiel"];

    /// <summary>
    /// Story 3-6: Übernahme von „Lagerfeuer“ aus der Programmbibliothek in die Kinderbibeltage (nur, wenn es den Titel dort noch nicht gibt).
    /// </summary>
    public static (string Titel, DateOnly Tag, string Beginn) BibliotheksUebernahme { get; } = ("Lagerfeuer", new DateOnly(2027, 8, 11), "19:30");

    /// <summary>
    /// Story 3-8: zwei vergangene Freizeiten der Demo-Organisation mit festen Daten (der Host läuft mit der Systemuhr; feste Daten halten die
    /// Phasen und die Bilder der Doku stabil), Leitung ist <see cref="LeitungVergangen"/>. Die Herbstfreizeit bleibt in der Nachbereitung, mit
    /// <see cref="HerbstfreizeitAufgaben"/> als offenen Aufgaben; das Pfingstlager hat Schäden und Verluste sowie eine Vorfall-Notiz und ist
    /// abgeschlossen. Ein Team gibt es dort nicht: Team entsteht nur über die Anmeldung mit dem Anmeldelink, und der gilt nur bis Freizeitende.
    /// </summary>
    public static (string Name, DateOnly Beginn, DateOnly Ende) Herbstfreizeit { get; } = ("Herbstfreizeit 2026", new DateOnly(2026, 10, 3), new DateOnly(2026, 10, 8));

    /// <summary>Story 3-8: siehe <see cref="Herbstfreizeit"/>.</summary>
    public static (string Name, DateOnly Beginn, DateOnly Ende) Pfingstlager { get; } = ("Pfingstlager 2026", new DateOnly(2026, 5, 23), new DateOnly(2026, 5, 30));

    /// <summary>Story 3-8: Leitung der beiden vergangenen Freizeiten.</summary>
    public const string LeitungVergangen = "miriam.zeltner@example.org";

    /// <summary>Story 3-8: zwei offene Aufgaben der Nachbereitung in der Herbstfreizeit (je nur, wenn es den Titel dort noch nicht gibt).</summary>
    public static IReadOnlyList<string> HerbstfreizeitAufgaben { get; } = ["Fotos an die Familien schicken", "Abrechnung bei der Organisation abgeben"];

    /// <summary>Story 3-8: Schäden und Verluste des Pfingstlagers; nach dem Abschluss sind sie Anschaffungswünsche der Organisation.</summary>
    public static IReadOnlyList<(Zeltlotse.Api.Contracts.Inventar.SchadenArt Art, string Bezeichnung)> PfingstlagerSchaeden { get; } =
    [
        (Zeltlotse.Api.Contracts.Inventar.SchadenArt.Schaden, "Kochtopf: Henkel abgebrochen"),
        (Zeltlotse.Api.Contracts.Inventar.SchadenArt.Verlust, "Zeltheringe (12 Stück)"),
    ];

    /// <summary>
    /// Story 3-8: Vorfall-Notiz im Pfingstlager (feste Kennung, damit das Einspielen idempotent bleibt). Betroffen ist die Leitung selbst, denn
    /// sonst gehört dort niemand zur Freizeit; nach dem Abschluss steht sie im Archiv der Organisation.
    /// </summary>
    public static (Guid Id, string Text) PfingstlagerVorfall { get; } =
        (Guid.Parse("0199e8a0-0000-7000-8000-00000000f101"), "Beim Zeltabbau an der Hand geschnitten. Wunde gereinigt und verbunden.");

    /// <summary>
    /// Jonas' Nachweise (Story 1-5): Art, Ausstellungsdatum und eine kleine erfundene Demo-Datei. Ohne Präventionsschulung, damit
    /// „Nachweis hinzufügen“ in der Doku sichtbar bleibt.
    /// </summary>
    public static IReadOnlyList<(NachweisArt Art, DateOnly Ausstellung, Func<byte[]> Datei)> Nachweise { get; } =
    [
        (NachweisArt.Fuehrungszeugnis, new DateOnly(2025, 11, 3), () => DemoDateien.Pdf("Demo-Nachweis: Fuehrungszeugnis (erfunden)")),
        (NachweisArt.Juleica, new DateOnly(2024, 5, 20), () => DemoDateien.Pdf("Demo-Nachweis: JuLeiCa (erfunden)")),
        (NachweisArt.ErsteHilfe, new DateOnly(2025, 4, 15), DemoDateien.Png),
    ];

    public static IReadOnlyList<DemoPerson> Personen { get; } =
    [
        new("Gerd", "Betreiber", DemoRolle.GlobalAdmin, "gerd.betreiber@example.org"),
        new("Andrea", "Beispiel", DemoRolle.OrgAdmin, "andrea.beispiel@example.org"),
        new("Jonas", "Hüttner", DemoRolle.Leitung, "jonas.huettner@example.org", Herkunft: "Musterstadt"),
        new("Miriam", "Zeltner", DemoRolle.Leitung, "miriam.zeltner@example.org", Herkunft: "Musterstadt"),
        new("Lea", "Feuerstein", DemoRolle.Team, "lea.feuerstein@example.org"),
        new("Tobias", "Wanderer", DemoRolle.Team, "tobias.wanderer@example.org"),
        new("Sara", "Kochlöffel", DemoRolle.Team, "sara.kochloeffel@example.org"),
        new("Katrin", "Muster", DemoRolle.Eltern, "katrin.muster@example.org", Familie: "Muster"),
        new("Paul", "Muster", DemoRolle.Kind, Mail: null, Familie: "Muster", Geburtsdatum: new DateOnly(2008, 3, 14)),
        new("Emma", "Muster", DemoRolle.Kind, Mail: null, Familie: "Muster", Geburtsdatum: new DateOnly(2017, 9, 2)),
        new("Daniel", "Probst", DemoRolle.Eltern, "daniel.probst@example.org", Familie: "Probst"),
        new("Mia", "Probst", DemoRolle.Kind, Mail: null, Familie: "Probst", Geburtsdatum: new DateOnly(2016, 5, 21)),
        new("Tim", "Kröger", DemoRolle.LeitungZweiteFreizeit, "tim.kroeger@example.org"),
    ];
}

internal sealed record DemoOrganisation(string Name);

/// <summary>Kleine erfundene Demo-Dateien für Nachweise, im Code erzeugt (keine echten Dokumente).</summary>
internal static class DemoDateien
{
    /// <summary>Ein einseitiges PDF mit einer Zeile Text (nur ASCII) und gültiger Querverweistabelle.</summary>
    public static byte[] Pdf(string zeile)
    {
        var text = zeile.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("(", @"\(", StringComparison.Ordinal).Replace(")", @"\)", StringComparison.Ordinal);
        var inhalt = $"BT /F1 18 Tf 60 760 Td ({text}) Tj ET";
        string[] objekte =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {inhalt.Length} >>\nstream\n{inhalt}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        ];
        var kultur = System.Globalization.CultureInfo.InvariantCulture;
        var pdf = new System.Text.StringBuilder("%PDF-1.4\n");
        var positionen = new List<int>();
        for (var i = 0; i < objekte.Length; i++)
        {
            positionen.Add(pdf.Length);
            pdf.Append(kultur, $"{i + 1} 0 obj\n{objekte[i]}\nendobj\n");
        }

        var xref = pdf.Length;
        pdf.Append(kultur, $"xref\n0 {objekte.Length + 1}\n0000000000 65535 f \n");
        foreach (var position in positionen)
        {
            pdf.Append(kultur, $"{position:D10} 00000 n \n");
        }

        pdf.Append(kultur, $"trailer\n<< /Size {objekte.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return System.Text.Encoding.ASCII.GetBytes(pdf.ToString());
    }

    /// <summary>Ein PNG (240 × 160) mit blaugrüner Fläche und hellem Rahmen, als Platzhalter für ein Foto.</summary>
    public static byte[] Png()
    {
        const int breite = 240;
        const int hoehe = 160;
        var roh = new byte[hoehe * ((breite * 3) + 1)];
        for (var y = 0; y < hoehe; y++)
        {
            var zeile = y * ((breite * 3) + 1);
            for (var x = 0; x < breite; x++)
            {
                var rand = x < 8 || y < 8 || x >= breite - 8 || y >= hoehe - 8;
                var (r, g, b) = rand ? ((byte)230, (byte)242, (byte)241) : ((byte)0, (byte)110, (byte)115);
                roh[zeile + 1 + (x * 3)] = r;
                roh[zeile + 2 + (x * 3)] = g;
                roh[zeile + 3 + (x * 3)] = b;
            }
        }

        using var gepackt = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(gepackt, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(roh);
        }

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var kopf = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(kopf, breite);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(kopf.AsSpan(4), hoehe);
        kopf[8] = 8; // Bittiefe
        kopf[9] = 2; // RGB
        Block(png, "IHDR", kopf);
        Block(png, "IDAT", gepackt.ToArray());
        Block(png, "IEND", []);
        return png.ToArray();
    }

    private static void Block(Stream ziel, string typ, byte[] daten)
    {
        var laenge = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(laenge, daten.Length);
        ziel.Write(laenge);
        byte[] typUndDaten = [.. System.Text.Encoding.ASCII.GetBytes(typ), .. daten];
        ziel.Write(typUndDaten);
        var pruefsumme = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(pruefsumme, Crc32(typUndDaten));
        ziel.Write(pruefsumme);
    }

    private static uint Crc32(byte[] daten)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in daten)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}

/// <param name="Beschreibung">Text der Freizeitseite (Story 2-1).</param>
/// <param name="HoechstzahlTeilnehmende">Höchstzahl Teilnehmende auf der Freizeitseite (Story 2-1).</param>
/// <param name="Teilnahmebedingungen">Text der Teilnahmebedingungen im Anmeldeformular (Story 2-2).</param>
internal sealed record DemoFreizeit(
    string Name, string Ort, DateOnly Beginn, DateOnly Ende, string Beschreibung, int HoechstzahlTeilnehmende, string Teilnahmebedingungen);

/// <summary>
/// Rolle einer Demo-Person; <see cref="Kind"/> ist ein Kinderprofil im Familienkonto, ohne eigenes Konto (Paul ist schon
/// volljährig, damit Screenshot und Doku „Profil an Paul übergeben“ zeigen, NS-11).
/// <see cref="GlobalAdmin"/> ist der Betreiber (nur bei aktiven Demo-Daten Global Admin).
/// </summary>
internal enum DemoRolle
{
    GlobalAdmin,
    OrgAdmin,
    Leitung,
    Team,
    Eltern,
    Kind,

    /// <summary>Story 3-3: Leitung der <see cref="DemoKatalog.ZweiteFreizeit"/>, nicht des Zeltlagers.</summary>
    LeitungZweiteFreizeit,
}

/// <param name="Mail">Anmelde-Adresse; nur Erwachsene haben eine.</param>
/// <param name="Familie">Familienkonto, zu dem Eltern und Kinder gehören.</param>
/// <param name="Herkunft">Wohnort im Profil (Story 2-1), erscheint auf der Freizeitseite.</param>
internal sealed record DemoPerson(
    string Vorname,
    string Nachname,
    DemoRolle Rolle,
    string? Mail,
    string? Familie = null,
    DateOnly? Geburtsdatum = null,
    string? Herkunft = null);

/// <summary>Eine Demo-Anmeldung mit Konto (Story 2-2, 2-3).</summary>
/// <param name="Mail">Konto, das anmeldet: Elternteil oder die Person selbst.</param>
/// <param name="Kind">Vorname des Kindes aus der Familie; leer heißt: selbst, über den Anmeldelink für Mitarbeitende.</param>
/// <param name="Bestaetigt">Miriam hat die Anmeldung bestätigt.</param>
internal sealed record DemoAnmeldung(string Mail, string? Kind, bool Bestaetigt);

/// <summary>Story 4-1: Ein Rezept der Demo-Küche.</summary>
internal sealed record DemoRezept(string Name, int BasisPersonen, string Zubereitung, IReadOnlyList<DemoZutat> Zutaten);

/// <summary>Story 4-1: Eine Zutat eines Demo-Rezepts; <c>Menge</c> als Text wie im Formular, <c>Allergene</c> als Komma-Liste der Schlüssel.</summary>
internal sealed record DemoZutat(string Bezeichnung, string? Menge, string? Einheit, string? Allergene);
