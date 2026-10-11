namespace Zeltlotse.SharedKernel;

/// <summary>Kennungen vergibt der Server als UUID Version 7 aus der Uhr (Konvention „Kennungen“).</summary>
public static class Kennung
{
    public static Guid Neu(TimeProvider uhr) => Guid.CreateVersion7(uhr.GetUtcNow());
}
