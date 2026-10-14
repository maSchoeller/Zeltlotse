using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Zeltlotse.Api.Contracts.Gemeinsam;

namespace Zeltlotse.Inventar;

/// <summary>Story 3-3: Problem-Antworten (<c>409</c>) des Inventars mit den vorhandenen Problemcodes.</summary>
internal static class InventarProbleme
{
    public static ProblemHttpResult Problem(string code, string titel) => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: titel,
        extensions: new Dictionary<string, object?> { [Problemcodes.Feld] = code });

    public static ProblemHttpResult Konflikt(string titel) => Problem(Problemcodes.Konflikt, titel);

    public static ProblemHttpResult Abgeschlossen() => Problem(Problemcodes.FreizeitAbgeschlossen, "Diese Freizeit ist abgeschlossen und nur lesbar.");
}
