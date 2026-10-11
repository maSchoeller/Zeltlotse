namespace Zeltlotse.Organisationen.Domaene;

/// <summary>
/// Organisation (FR-5): der Mandant selbst, darum nicht organisationseigen und ohne Mandantenfilter (AD-6).
/// <see cref="EingerichtetAm"/> setzt die erste angenommene Einladung als Org-Admin.
/// </summary>
internal sealed class Organisation
{
    public const int MaximaleNamensLaenge = 100;

    private Organisation()
    {
    }

    public Guid Id { get; private init; }

    /// <summary>Getrimmt, 1 bis <see cref="MaximaleNamensLaenge"/> Zeichen.</summary>
    public string Name { get; private init; } = "";

    public DateTimeOffset AngelegtAm { get; private init; }

    /// <summary>Wann der erste Org-Admin seine Einladung angenommen hat; <c>null</c> bis dahin.</summary>
    public DateTimeOffset? EingerichtetAm { get; private set; }

    public static Organisation Anlegen(Guid id, string name, DateTimeOffset jetzt) => new()
    {
        Id = id,
        Name = name.Trim(),
        AngelegtAm = jetzt,
    };

    /// <summary>Ein Org-Admin hat angenommen; nur das erste Mal zählt.</summary>
    public void Einrichten(DateTimeOffset jetzt) => EingerichtetAm ??= jetzt;
}
