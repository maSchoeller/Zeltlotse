namespace Zeltlotse.Api.Contracts.Gemeinsam;

/// <summary>HTTP-Kopfzeilen, die Clients an die API schicken (AD-5, AD-25).</summary>
public static class Kopfzeilen
{
    /// <summary>Antiforgery-Token aus <c>GET /api/betrieb/antiforgery</c>; Pflicht für POST, PUT, PATCH und DELETE unter <c>/api</c>.</summary>
    public const string Antiforgery = "X-Zeltlotse-Antiforgery";

    /// <summary>Dienst-Kennung der öffentlichen Seiten; nur aus dem eigenen Prozess gültig.</summary>
    public const string Dienst = "X-Zeltlotse-Dienst";

    /// <summary>Die durchgereichte IP des Besuchers der öffentlichen Seiten; zählt nur mit gültiger Dienst-Kennung.</summary>
    public const string BesucherIp = "X-Zeltlotse-Besucher-Ip";
}
