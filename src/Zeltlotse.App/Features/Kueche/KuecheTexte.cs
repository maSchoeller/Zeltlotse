using Zeltlotse.Api.Contracts.Kueche;
using Zeltlotse.Ui.Komponenten;
using Zeltlotse.Ui.Texte;

namespace Zeltlotse.App.Features.Kueche;

/// <summary>
/// Story 4-1: Gemeinsames der Küchen-Seiten: Texte, Ziele der Links und die Allergene als Auswahl. Story 4-2: Essensplan und Einkaufsliste. Story 4-3:
/// Einkauf, Freigabe und „schon gekauft“.
/// </summary>
internal static class KuecheTexte
{
    public const string AbgeschlossenText = "Diese Freizeit ist abgeschlossen und nur lesbar.";

    /// <summary>Hinweis, solange niemand bestätigt ist (Personenzahl 0): Es gibt nichts hochzurechnen.</summary>
    public const string KeinePersonenText = "Noch niemand bestätigt – Mengen gelten für die Basis.";

    /// <summary>„Hochgerechnet auf 6 Personen der Freizeit.“; bei 0 Personen <see cref="KeinePersonenText"/>.</summary>
    public static string Hochgerechnet(int personenzahl) => personenzahl switch
    {
        0 => KeinePersonenText,
        1 => "Hochgerechnet auf 1 Person der Freizeit.",
        _ => $"Hochgerechnet auf {personenzahl} Personen der Freizeit.",
    };

    /// <summary>Die Rezepte der Freizeit.</summary>
    public static string RezepteZiel(Guid freizeitId) => $"freizeiten/{freizeitId}/kueche/rezepte";

    /// <summary>Ein Rezept der Freizeit.</summary>
    public static string RezeptZiel(Guid freizeitId, Guid rezeptId) => $"{RezepteZiel(freizeitId)}/{rezeptId}";

    /// <summary>Story 4-2: Satz, wenn die Freizeit die Allergie-Frage nicht abfragt (kein Abgleich).</summary>
    public const string KeineAllergienText = "Diese Freizeit fragt keine Allergien ab.";

    /// <summary>Story 4-2: Satz, wenn kein bestätigter Freizeitpass ein Allergen nennt.</summary>
    public const string KeineAllergeneText = "Keine Allergien in den bestätigten Freizeitpässen.";

    /// <summary>Story 4-2: der Essensplan, mit Tag als <c>?tag=JJJJ-MM-TT</c>.</summary>
    public static string EssensplanZiel(Guid freizeitId, DateOnly? tag = null) =>
        tag is { } wert ? $"freizeiten/{freizeitId}/kueche/essensplan?tag={Iso(wert)}" : $"freizeiten/{freizeitId}/kueche/essensplan";

    /// <summary>Story 4-2: die Einkaufsliste, mit Tag als Sprungziel der Card dieses Tages.</summary>
    public static string EinkaufslisteZiel(Guid freizeitId, DateOnly? tag = null) =>
        tag is { } wert ? $"freizeiten/{freizeitId}/kueche/einkaufsliste#{TagAnker(wert)}" : $"freizeiten/{freizeitId}/kueche/einkaufsliste";

    /// <summary>Story 4-3: die Seite der Einkaufenden.</summary>
    public static string EinkaufZiel(Guid freizeitId) => $"freizeiten/{freizeitId}/kueche/einkauf";

    /// <summary>
    /// Story 4-3: Ein Posten in einer Zeile, z. B. „Mehl 500 g · Pfannkuchen · Frühstück“; Hand-Posten ohne Gericht, z. B. „Äpfel 3 kg“, ohne Menge nur
    /// die Bezeichnung.
    /// </summary>
    public static string PostenText(EinkaufslistenPosten posten) =>
        $"{posten.Bezeichnung} {MengeMitEinheit(posten.Menge, posten.Einheit)}".Trim()
        + (posten.RezeptName is null ? "" : $" · {posten.RezeptName} · {posten.MahlzeitName}");

    /// <summary>Story 4-3: Zählzeile eines Tages auf der Einkaufsliste, z. B. „1 von 3 freigegeben · 0 gekauft“.</summary>
    public static string Freigabezeile(IReadOnlyCollection<EinkaufslistenPosten> posten) =>
        $"{posten.Count(p => p.Freigegeben)} von {posten.Count} freigegeben · {posten.Count(p => p.Gekauft)} gekauft";

    /// <summary>Story 4-3: Satz der Tausch-Vorschau zu schon gekauften Posten; ohne solche <c>null</c>.</summary>
    public static string? SchonGekauft(int anzahl) => anzahl switch
    {
        <= 0 => null,
        1 => "1 schon gekaufter Posten steht nicht im Vergleich.",
        _ => $"{anzahl} schon gekaufte Posten stehen nicht im Vergleich.",
    };

    /// <summary>Story 4-2: Kennung der Card eines Tages auf der Einkaufsliste, z. B. „tag-2027-08-03“; ohne Tag „tag-ohne“.</summary>
    public static string TagAnker(DateOnly? tag) => tag is { } wert ? $"tag-{Iso(wert)}" : "tag-ohne";

    public static string Iso(DateOnly tag) => tag.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Story 4-2: „Berechnet für 8 Personen der Freizeit.“; bei 0 Personen <see cref="KeinePersonenText"/>.</summary>
    public static string Berechnet(int personenzahl) => personenzahl switch
    {
        0 => KeinePersonenText,
        1 => "Berechnet für 1 Person der Freizeit.",
        _ => $"Berechnet für {personenzahl} Personen der Freizeit.",
    };

    /// <summary>Story 4-2: „1 Ersatzportion einplanen (enthält Gluten)“, „2 Ersatzportionen einplanen (enthält Gluten und Milch)“.</summary>
    public static string Ersatzportionen(Ersatzportionen ersatz) =>
        $"{(ersatz.Anzahl == 1 ? "1 Ersatzportion" : $"{ersatz.Anzahl} Ersatzportionen")} einplanen (enthält {Anzeige.Aufzaehlung(Allergene.Kurztexte(ersatz.Allergene))})";

    /// <summary>Story 4-2: „Gluten: 1 Person“, „Milch: 2 Personen“.</summary>
    public static string AllergenZeile(AllergenAnzahl allergen) =>
        $"{Allergene.Kurztexte([allergen.Allergen]).FirstOrDefault() ?? allergen.Allergen}: {Anzeige.Anzahl(allergen.Anzahl, "Person", "Personen")}";

    /// <summary>Story 4-2: Menge mit Einheit, z. B. „1,5 kg“; ohne Menge nur die Einheit oder leer.</summary>
    public static string MengeMitEinheit(decimal? menge, string? einheit) => $"{Anzeige.Menge(menge)} {einheit}".Trim();

    /// <summary>Die 14 EU-Hauptallergene als Kästchen der Autosave-Auswahl.</summary>
    public static IReadOnlyList<AuswahlOption> AllergenOptionen { get; } = [.. Allergene.Alle.Select(a => new AuswahlOption(a.Wert, a.Text))];
}
