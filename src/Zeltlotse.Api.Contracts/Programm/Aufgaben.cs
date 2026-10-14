using System.Text.Json.Serialization;

namespace Zeltlotse.Api.Contracts.Programm;

/// <summary>Story 3-5: Phase, in die eine Aufgabe gehört (FR-39, NS-4, NS-12); im JSON als Name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AufgabenPhase>))]
public enum AufgabenPhase
{
    /// <summary>Vor der Freizeit.</summary>
    Vorbereitung = 1,

    /// <summary>Während der Freizeit.</summary>
    Waehrend = 2,

    /// <summary>Nach der Freizeit bis zum Abschluss.</summary>
    Nachbereitung = 3,
}

/// <summary>
/// Story 3-5: Eine Aufgabe der Freizeit (FR-39, CAP-23) in <c>GET /api/freizeiten/{freizeitId}/programm/aufgaben</c> und den Antworten der
/// schreibenden Endpunkte.
/// </summary>
/// <param name="Version">Versionsnummer für den Autosave (AD-19).</param>
/// <param name="ProgrammpunktId">Der Programmpunkt, an dem die Aufgabe hängt, oder <c>null</c>.</param>
/// <param name="UebernommenVon">Die Person, die die Aufgabe übernommen hat; den Namen setzt der Client zusammen (AD-2).</param>
/// <param name="Meins">Die angemeldete Person hat die Aufgabe übernommen.</param>
/// <param name="Erledigt">Abgehakt (ohne Vermerk, wer).</param>
/// <param name="Vorlage">Die Aufgabe ist mit einer Aufgaben-Vorlage der Organisation verknüpft, die es noch gibt.</param>
public sealed record Aufgabe(
    Guid Id,
    uint Version,
    string Titel,
    AufgabenPhase Phase,
    Guid? ProgrammpunktId,
    Guid? UebernommenVon,
    bool Meins,
    bool Erledigt,
    bool Vorlage);

/// <summary>Story 3-5: Körper von <c>POST /api/freizeiten/{freizeitId}/programm/aufgaben</c>: nur der Titel ist nötig.</summary>
/// <param name="Titel">1 bis <see cref="AufgabenFelder.TitelMaximaleLaenge"/> Zeichen; wird getrimmt.</param>
/// <param name="Phase">Ohne Angabe <see cref="AufgabenPhase.Vorbereitung"/>.</param>
/// <param name="ProgrammpunktId">Optional ein Programmpunkt derselben Freizeit.</param>
public sealed record AufgabeNeu(string? Titel, AufgabenPhase? Phase, Guid? ProgrammpunktId);

/// <summary>Story 3-5: Eine Aufgaben-Vorlage der Organisation (FR-40); neue Freizeiten übernehmen sie beim ersten Öffnen der Aufgaben.</summary>
/// <param name="Version">Versionsnummer für den Autosave (AD-19).</param>
public sealed record AufgabenVorlage(Guid Id, uint Version, string Titel, AufgabenPhase Phase);

/// <summary>Story 3-5: Körper von <c>POST /api/organisationen/{orgId}/aufgaben-vorlagen</c>.</summary>
/// <param name="Phase">Ohne Angabe <see cref="AufgabenPhase.Vorbereitung"/>.</param>
public sealed record AufgabenVorlageNeu(string? Titel, AufgabenPhase? Phase);

/// <summary>Story 3-5: Bearbeitbare Felder einer Aufgabe für <c>PATCH …/programm/aufgaben/{aufgabeId}</c> (AD-19).</summary>
public enum AufgabeFeld
{
    Titel,
    Phase,
}

/// <summary>Story 3-5: Bearbeitbare Felder einer Aufgaben-Vorlage für <c>PATCH …/aufgaben-vorlagen/{vorlageId}</c> (AD-19).</summary>
public enum AufgabenVorlageFeld
{
    Titel,
    Phase,
}

/// <summary>
/// Story 3-5: JSON-Namen und Grenzen der Felder von Aufgabe und Aufgaben-Vorlage. <c>PATCH</c>: Körper
/// <c>{ "version": n, "alterWert": …, "titel"|"phase": neu }</c> mit genau einem Feld als Text (die Phase als Name, z. B. <c>Waehrend</c>),
/// Antwort <c>{ "version": n, "&lt;feld&gt;": gespeichert }</c>.
/// </summary>
public static class AufgabenFelder
{
    public const string Titel = "titel";

    public const string Phase = "phase";

    public const string ProgrammpunktId = "programmpunktId";

    public const int TitelMaximaleLaenge = 120;

    public static string JsonName(AufgabeFeld feld) => feld switch
    {
        AufgabeFeld.Titel => Titel,
        AufgabeFeld.Phase => Phase,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };

    public static string JsonName(AufgabenVorlageFeld feld) => feld switch
    {
        AufgabenVorlageFeld.Titel => Titel,
        AufgabenVorlageFeld.Phase => Phase,
        _ => throw new ArgumentOutOfRangeException(nameof(feld), feld, null),
    };

    /// <summary>Die Phase als Text, wie die API sie im <c>PATCH</c> liefert (Name des Enum-Werts).</summary>
    public static string PhaseText(AufgabenPhase phase) => phase.ToString();

    /// <summary>Liest eine Phase aus ihrem Namen (Groß- und Kleinschreibung egal); Zahlen und unbekannte Namen sind keine Phase.</summary>
    public static bool PhaseLesen(string? text, out AufgabenPhase phase)
    {
        phase = default;
        var name = text?.Trim();
        foreach (var wert in Enum.GetValues<AufgabenPhase>())
        {
            if (string.Equals(wert.ToString(), name, StringComparison.OrdinalIgnoreCase))
            {
                phase = wert;
                return true;
            }
        }

        return false;
    }
}
