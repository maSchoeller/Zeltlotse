namespace Zeltlotse.SharedKernel.Datenschutz;

/// <summary>
/// Platzhalter für eine gelöschte Person (AD-16): Bezüge in Daten anderer (z. B. „geprüft von“, „angefragt von“) tragen nach dem
/// Löschen diese feste Kennung statt der <c>PersonId</c>. Zu ihr gibt es nie ein Profil.
/// </summary>
public static class GeloeschtePerson
{
    public static readonly Guid Id = Guid.Parse("00000000-0000-7000-8000-0000000de1e7");
}
