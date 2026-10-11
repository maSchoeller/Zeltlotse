namespace Zeltlotse.Ui.Komponenten;

/// <summary>Eindeutige Element-Kennungen für <c>id</c>, <c>for</c> und <c>aria-*</c>.</summary>
internal static class Kennungen
{
    public static string Neu(string praefix) => $"{praefix}-{Guid.NewGuid():N}"[..(praefix.Length + 13)];
}
