using System.Text.Json;

namespace Zeltlotse.Ui.Api;

/// <summary>JSON-Einstellungen der API (Namen in camelCase, wie die Minimal APIs des Hosts).</summary>
internal static class ApiJson
{
    public static readonly JsonSerializerOptions Optionen = new(JsonSerializerDefaults.Web);
}
