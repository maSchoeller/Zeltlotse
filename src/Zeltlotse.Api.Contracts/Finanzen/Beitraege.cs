namespace Zeltlotse.Api.Contracts.Finanzen;

/// <summary>
/// Story 5-3 (CAP-34, FR-60, NS-8): Antwort von <c>GET /api/freizeiten/{freizeitId}/finanzen/beitraege</c> (Leitung und Team, Modul Finanzen): je bestätigter
/// teilnehmender Person der Freizeit eine Zeile, dazu „x von y bezahlt“. Mitarbeitende, eingegangene und abgesagte Anmeldungen stehen nicht darin; ein
/// Vermerk einer Person, die nicht mehr bestätigt teilnimmt, bleibt gespeichert, zählt aber nicht mit.
/// </summary>
/// <param name="Zeilen">Je bestätigter teilnehmender Person eine Zeile; die Namen setzt der Client aus der Personenliste zusammen (AD-2).</param>
/// <param name="Bezahlt">Wie viele Zeilen den Haken „bezahlt“ tragen.</param>
/// <param name="Gesamt">Wie viele Zeilen es gibt.</param>
public sealed record Beitraege(IReadOnlyList<BeitragZeile> Zeilen, int Bezahlt, int Gesamt);

/// <summary>
/// Story 5-3: Der Beitrag einer teilnehmenden Person, in <see cref="Beitraege"/> und als Antwort von <c>POST …/beitraege/{personId}/bezahlt</c> und
/// <c>…/offen</c>.
/// </summary>
/// <param name="PersonId">Die teilnehmende Person (AD-7).</param>
/// <param name="Bezahlt">Ob der Beitrag als bezahlt abgehakt ist.</param>
/// <param name="AbgehaktVon">
/// Wer den Haken gesetzt hat (Leitung oder Team); ohne Haken <c>null</c>. Nach dem Löschen dieser Person steht hier
/// <see cref="BelegFelder.GeloeschtePerson"/>.
/// </param>
/// <param name="AbgehaktAm">Wann der Haken gesetzt wurde; ohne Haken <c>null</c>.</param>
public sealed record BeitragZeile(Guid PersonId, bool Bezahlt, Guid? AbgehaktVon, DateTimeOffset? AbgehaktAm);
