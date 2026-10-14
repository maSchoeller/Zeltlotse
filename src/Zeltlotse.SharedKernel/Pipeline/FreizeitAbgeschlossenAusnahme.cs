namespace Zeltlotse.SharedKernel.Pipeline;

/// <summary>
/// Story 3-8 (AD-17): Ein Benutzer-Command betrifft eine gelaufene Freizeit. Der API-Host antwortet darauf mit <c>409</c> und dem Code
/// <c>freizeit-abgeschlossen</c>.
/// </summary>
public sealed class FreizeitAbgeschlossenAusnahme(Guid freizeitId)
    : Exception("Diese Freizeit ist abgeschlossen und nur lesbar.")
{
    public Guid FreizeitId { get; } = freizeitId;
}
