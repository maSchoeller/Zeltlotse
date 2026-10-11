namespace Zeltlotse.Ui.Transport;

/// <summary>
/// Hält fest, ob der Server eine andere Version als die laufende App meldet (AD-5). Der <see cref="VersionsHandler"/>
/// setzt den Hinweis, das Layout der App zeigt dann „Hinweis: Eine neue Version von Zeltlotse ist da.“
/// </summary>
public sealed class NeueVersion
{
    /// <summary>Der Server meldet eine andere Version.</summary>
    public bool Da { get; private set; }

    /// <summary>Wird einmal ausgelöst, sobald <see cref="Da"/> wahr wird.</summary>
    public event Action? Gemeldet;

    internal void Melden()
    {
        if (Da)
        {
            return;
        }

        Da = true;
        Gemeldet?.Invoke();
    }
}
