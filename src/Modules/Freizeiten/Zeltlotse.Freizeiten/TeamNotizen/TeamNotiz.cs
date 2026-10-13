using Zeltlotse.SharedKernel.Datenschutz;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.Freizeiten.TeamNotizen;

/// <summary>
/// Team-Notiz zu einer Anmeldung (Story 2-5, FR-26): Leitung und Team halten fest, was sie über eine Person wissen sollten. Sie gehört der
/// Freizeit, nicht der Person, und erscheint nie im Freizeitpass. Der Text liegt nur verschlüsselt mit dem Schlüssel der betroffenen
/// Person (AD-14). Unveränderlich; löschen darf jedes Mitglied von Leitung und Team. Lösch-Klasse „Arbeitsdaten der Freizeit“ (AD-16):
/// Mit der Person verschwinden die Notizen über sie, der Verfasser wird zum Platzhalter; der Löschlauf löscht sie mit den Anmeldungen.
/// </summary>
internal sealed class TeamNotiz : IOrganisationsEigen
{
    private TeamNotiz()
    {
    }

    public Guid Id { get; private init; }

    public Guid OrganisationId { get; private init; }

    public Guid FreizeitId { get; private init; }

    public Guid AnmeldungId { get; private init; }

    /// <summary>Die betroffene Person (die Person der Anmeldung).</summary>
    public Guid PersonId { get; private init; }

    /// <summary>Wer die Notiz geschrieben hat; nach dessen Löschung der Platzhalter <see cref="GeloeschtePerson.Id"/>.</summary>
    public Guid VerfasstVon { get; private init; }

    /// <summary>Der Text, verschlüsselt mit dem Schlüssel der betroffenen Person.</summary>
    [Besonders]
    public Geschuetzt Text { get; private init; } = null!;

    public DateTimeOffset AngelegtAm { get; private init; }

    public static TeamNotiz Anlegen(
        Guid id, Guid organisationId, Guid freizeitId, Guid anmeldungId, Guid personId, Guid verfasstVon, Geschuetzt text, DateTimeOffset jetzt)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.PersonId != personId)
        {
            throw new InvalidOperationException("Der Text ist nicht mit dem Schlüssel der betroffenen Person verschlüsselt.");
        }

        return new TeamNotiz
        {
            Id = id,
            OrganisationId = organisationId,
            FreizeitId = freizeitId,
            AnmeldungId = anmeldungId,
            PersonId = personId,
            VerfasstVon = verfasstVon,
            Text = text,
            AngelegtAm = jetzt,
        };
    }
}
