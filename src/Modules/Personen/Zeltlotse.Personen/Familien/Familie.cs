namespace Zeltlotse.Personen.Familien;

/// <summary>
/// Familienkonto (FR-3, AD-7): Eltern verwalten darin die Profile ihrer Kinder unter 18. Entsteht beim ersten „Kind
/// hinzufügen“ oder „Elternteil einladen“; die anfragende Person ist dann ihr erster Elternteil. Nicht organisationseigen.
/// </summary>
internal sealed class Familie
{
    /// <summary>Eine Familie hat höchstens so viele Eltern, alle mit gleichen Rechten (AD-7).</summary>
    public const int HoechstensEltern = 2;

    private Familie()
    {
    }

    public Guid Id { get; private init; }

    public DateTimeOffset AngelegtAm { get; private init; }

    public static Familie Anlegen(Guid id, DateTimeOffset jetzt) => new() { Id = id, AngelegtAm = jetzt };
}

/// <summary>
/// Ein Elternteil einer Familie. Eine Person ist Elternteil in höchstens einer Familie (Patchwork ist nicht abgebildet);
/// die Datenbank sichert das mit einem eindeutigen Index auf <see cref="PersonId"/>.
/// </summary>
internal sealed class FamilienElternteil
{
    private FamilienElternteil()
    {
    }

    public Guid FamilieId { get; private init; }

    public Guid PersonId { get; private init; }

    public static FamilienElternteil Anlegen(Guid familieId, Guid personId) => new() { FamilieId = familieId, PersonId = personId };
}
