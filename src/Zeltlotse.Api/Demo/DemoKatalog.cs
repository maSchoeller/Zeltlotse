namespace Zeltlotse.Api.Demo;

/// <summary>
/// Erfundene Stammdaten für Screenshots und Anwender-Doku (NFR-20, Konvention „Anwender-Doku“): eine Organisation,
/// eine Freizeit und Personen mit Rolle. Alle Mail-Adressen enden auf <c>@example.org</c>; keine Telefonnummern,
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
        new DateOnly(2027, 8, 13));

    public static IReadOnlyList<DemoPerson> Personen { get; } =
    [
        new("Gerd", "Betreiber", DemoRolle.GlobalAdmin, "gerd.betreiber@example.org"),
        new("Andrea", "Beispiel", DemoRolle.OrgAdmin, "andrea.beispiel@example.org"),
        new("Jonas", "Hüttner", DemoRolle.Leitung, "jonas.huettner@example.org"),
        new("Miriam", "Zeltner", DemoRolle.Leitung, "miriam.zeltner@example.org"),
        new("Lea", "Feuerstein", DemoRolle.Team, "lea.feuerstein@example.org"),
        new("Tobias", "Wanderer", DemoRolle.Team, "tobias.wanderer@example.org"),
        new("Sara", "Kochlöffel", DemoRolle.Team, "sara.kochloeffel@example.org"),
        new("Katrin", "Muster", DemoRolle.Eltern, "katrin.muster@example.org", Familie: "Muster"),
        new("Paul", "Muster", DemoRolle.Kind, Mail: null, Familie: "Muster", Geburtsdatum: new DateOnly(2008, 3, 14)),
        new("Emma", "Muster", DemoRolle.Kind, Mail: null, Familie: "Muster", Geburtsdatum: new DateOnly(2017, 9, 2)),
        new("Daniel", "Probst", DemoRolle.Eltern, "daniel.probst@example.org", Familie: "Probst"),
        new("Mia", "Probst", DemoRolle.Kind, Mail: null, Familie: "Probst", Geburtsdatum: new DateOnly(2016, 5, 21)),
    ];
}

internal sealed record DemoOrganisation(string Name);

internal sealed record DemoFreizeit(string Name, string Ort, DateOnly Beginn, DateOnly Ende);

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
}

/// <param name="Mail">Anmelde-Adresse; nur Erwachsene haben eine.</param>
/// <param name="Familie">Familienkonto, zu dem Eltern und Kinder gehören.</param>
internal sealed record DemoPerson(
    string Vorname,
    string Nachname,
    DemoRolle Rolle,
    string? Mail,
    string? Familie = null,
    DateOnly? Geburtsdatum = null);
