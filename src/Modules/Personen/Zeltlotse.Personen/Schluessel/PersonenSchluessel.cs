namespace Zeltlotse.Personen.Schluessel;

/// <summary>
/// Der Schlüssel einer Person (AD-14), mit dem Hauptschlüssel umhüllt. Wird er gelöscht, sind alle mit ihm
/// verschlüsselten Daten unlesbar, auch in Backups (Crypto-Shredding). Angelegt wird er ab Story 1-1 mit der Person.
/// </summary>
internal sealed class PersonenSchluessel
{
    private PersonenSchluessel()
    {
    }

    public Guid PersonId { get; private init; }

    /// <summary>Format und Inhalt bestimmt <c>Hauptschluessel</c>; der rohe Schlüssel steht nie in der Datenbank.</summary>
    public byte[] Umhuellt { get; private init; } = [];

    public DateTimeOffset ErstelltAm { get; private init; }

    public static PersonenSchluessel Anlegen(Guid personId, byte[] umhuellt, TimeProvider uhr) => new()
    {
        PersonId = personId,
        Umhuellt = umhuellt,
        ErstelltAm = uhr.GetUtcNow(),
    };
}
