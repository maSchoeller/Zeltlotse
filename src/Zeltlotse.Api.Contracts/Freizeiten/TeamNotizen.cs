namespace Zeltlotse.Api.Contracts.Freizeiten;

/// <summary>
/// Eine Team-Notiz von <c>GET /api/freizeiten/{freizeitId}/anmeldungen/{anmeldungId}/notizen</c> (Leitung und Team, Story 2-5, FR-26):
/// Text, wer sie verfasst hat (bei einer gelöschten Person „Gelöschte Person“) und wann. Neueste zuerst. Team-Notizen bleiben in der
/// Freizeit und erscheinen nie im Freizeitpass.
/// </summary>
public sealed record TeamNotiz(Guid Id, string Text, Guid VerfasstVon, string? VerfasserVorname, string? VerfasserNachname, DateTimeOffset AngelegtAm)
{
    /// <summary>Keinen Notiztext ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(TeamNotiz)}({Id})";
}

/// <summary>Körper von <c>POST /api/freizeiten/{freizeitId}/anmeldungen/{anmeldungId}/notizen</c>: nur der Text (Pflicht).</summary>
public sealed record TeamNotizAnlegen(string? Text)
{
    /// <summary>Keinen Notiztext ausgeben (AD-22).</summary>
    public override string ToString() => nameof(TeamNotizAnlegen);
}

/// <summary>Antwort <c>201</c> beim Anlegen einer Team-Notiz.</summary>
public sealed record TeamNotizAngelegt(Guid Id);

/// <summary>JSON-Namen und Grenzwert der Team-Notizen (Fehlerschlüssel in ProblemDetails, AD-27).</summary>
public static class TeamNotizFelder
{
    public const string Text = "text";

    public const int TextMaximaleLaenge = 2000;
}
