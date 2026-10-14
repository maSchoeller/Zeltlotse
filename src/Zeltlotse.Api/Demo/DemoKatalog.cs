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
    /// Medikamente für den Notfallordner.
    /// </summary>
    public static IReadOnlyList<(string Kennung, string Wert)> MiasFreizeitpass { get; } =
    [
        ("allergien", "gluten"),
        ("notfallkontakte", "Daniel Probst (Vater), 0123 456781"),
        ("schwimmen", "nein"),
        ("medikamente", "Asthmaspray bei Bedarf, liegt im Kulturbeutel"),
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
    /// Person, die ihn übernommen hat (Mail-Adresse) oder ohne. „Ich übernehme“ führt die Person selbst aus.
    /// </summary>
    public static IReadOnlyList<(string Titel, DateOnly Tag, TimeOnly Beginn, string? UebernommenVon)> Programmpunkte { get; } =
    [
        ("Anreise und Zeltaufbau", new DateOnly(2027, 8, 2), new TimeOnly(15, 0), null),
        ("Lagerfeuer", new DateOnly(2027, 8, 2), new TimeOnly(20, 0), "lea.feuerstein@example.org"),
        ("Geländespiel", new DateOnly(2027, 8, 3), new TimeOnly(14, 30), "jonas.huettner@example.org"),
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
