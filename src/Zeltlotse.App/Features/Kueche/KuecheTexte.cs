using Zeltlotse.Api.Contracts.Kueche;
using Zeltlotse.Ui.Komponenten;

namespace Zeltlotse.App.Features.Kueche;

/// <summary>Story 4-1: Gemeinsames der Küchen-Seiten: Texte, Ziele der Links und die Allergene als Auswahl.</summary>
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

    /// <summary>Die 14 EU-Hauptallergene als Kästchen der Autosave-Auswahl.</summary>
    public static IReadOnlyList<AuswahlOption> AllergenOptionen { get; } = [.. Allergene.Alle.Select(a => new AuswahlOption(a.Wert, a.Text))];
}
