using System.Globalization;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Api.Contracts.Programm;
using Zeltlotse.Programm.Daten;
using Zeltlotse.SharedKernel.Anmeldung;
using ApiAufgabe = Zeltlotse.Api.Contracts.Programm.Aufgabe;
using ApiVorlage = Zeltlotse.Api.Contracts.Programm.AufgabenVorlage;

namespace Zeltlotse.Programm.Aufgaben;

/// <summary>
/// <c>GET /api/freizeiten/{freizeitId}/programm/aufgaben</c> (Leitung und Team, FR-39): alle Aufgaben der Freizeit nach Phase, Titel und Kennung.
/// Beim ersten Laden übernimmt der interne Command <see cref="AufgabenVorlagenUebernehmen"/> vorher einmalig die Vorlagen der Organisation (nicht
/// nach dem Abschluss). Den Mandanten-Kontext hat der Host aus der Freizeit gesetzt.
/// </summary>
internal static class AufgabenLadenEndpunkt
{
    public static async Task<Ok<IReadOnlyList<ApiAufgabe>>> Laden(
        Guid freizeitId, ProgrammLeseDbContext db, ISender sender, IAngemeldet angemeldet, CancellationToken abbruch)
    {
        if (!await db.VorlagenUebernahmen.AnyAsync(u => u.FreizeitId == freizeitId, abbruch))
        {
            await sender.Send(new AufgabenVorlagenUebernehmen(freizeitId), abbruch);
        }

        var aufgaben = await db.Aufgaben.Where(a => a.FreizeitId == freizeitId).ToListAsync(abbruch);
        return TypedResults.Ok<IReadOnlyList<ApiAufgabe>>([.. AufgabenLesen.Sortiert(aufgaben).Select(a => AufgabenLesen.AlsApi(a, angemeldet.PersonId))]);
    }
}

/// <summary>Gemeinsame Lesart der Aufgaben und Vorlagen für die Listen und die Antworten der schreibenden Endpunkte.</summary>
internal static class AufgabenLesen
{
    private static readonly StringComparer Vergleich = StringComparer.Create(CultureInfo.GetCultureInfo("de-DE"), ignoreCase: true);

    /// <summary>Nach Phase, Titel und Kennung.</summary>
    public static IEnumerable<Aufgabe> Sortiert(IEnumerable<Aufgabe> aufgaben) =>
        aufgaben.OrderBy(a => a.Phase).ThenBy(a => a.Titel, Vergleich).ThenBy(a => a.Id);

    /// <summary>Nach Phase, Titel und Kennung.</summary>
    public static IEnumerable<AufgabenVorlage> Sortiert(IEnumerable<AufgabenVorlage> vorlagen) =>
        vorlagen.OrderBy(v => v.Phase).ThenBy(v => v.Titel, Vergleich).ThenBy(v => v.Id);

    /// <param name="ich">Die angemeldete Person; „Meins“ heißt: sie hat die Aufgabe übernommen.</param>
    public static ApiAufgabe AlsApi(Aufgabe aufgabe, Guid? ich) => new(
        aufgabe.Id,
        aufgabe.Version,
        aufgabe.Titel,
        aufgabe.Phase,
        aufgabe.ProgrammpunktId,
        aufgabe.Uebernommen,
        ich is { } person && aufgabe.Uebernommen == person,
        aufgabe.Erledigt,
        aufgabe.VorlageId is not null);

    public static ApiVorlage AlsApi(AufgabenVorlage vorlage) => new(vorlage.Id, vorlage.Version, vorlage.Titel, vorlage.Phase);
}

/// <summary>Texte der Formprüfung, gleich für Aufgaben und Vorlagen.</summary>
internal static class AufgabenTexte
{
    public const string UnbekanntePhase = "Phase: Vorbereitung, Waehrend oder Nachbereitung.";

    /// <summary>Der Fehler zum Titel oder <c>null</c>: Pflicht, getrimmt höchstens <see cref="AufgabenFelder.TitelMaximaleLaenge"/> Zeichen.</summary>
    public static string? TitelFehler(string? titel)
    {
        var text = titel?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return "Titel fehlt.";
        }

        return text.Length > AufgabenFelder.TitelMaximaleLaenge ? $"Höchstens {AufgabenFelder.TitelMaximaleLaenge} Zeichen." : null;
    }

    /// <summary>Eine angegebene Phase muss bekannt sein; ohne Angabe gilt Vorbereitung.</summary>
    public static bool PhaseUnbekannt(AufgabenPhase? phase) => phase is { } wert && !Enum.IsDefined(wert);
}
