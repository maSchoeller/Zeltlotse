using System.Net;
using System.Text;

namespace Zeltlotse.Ui.Tests;

/// <summary>Eine aufgezeichnete Anfrage mit gelesenem Körper und Kopfzeilen.</summary>
public sealed record Aufgezeichnet(HttpMethod Methode, string Pfad, string? Koerper, IReadOnlyDictionary<string, string> Kopfzeilen);

/// <summary>
/// Nachgebildeter <see cref="HttpMessageHandler"/>: zeichnet jede Anfrage samt Körper auf und beantwortet sie aus einer
/// Regel je Anfrage (Methode und Pfad). Eine Regel kann eine Antwort liefern, eine Ausnahme werfen oder warten.
/// </summary>
public sealed class NachgebildeterHandler : HttpMessageHandler
{
    private readonly List<Func<HttpRequestMessage, Task<HttpResponseMessage>?>> _regeln = [];

    public List<Aufgezeichnet> Anfragen { get; } = [];

    /// <summary>Antwortet auf Anfragen, die <paramref name="passt"/> erfüllen; spätere Regeln haben Vorrang.</summary>
    public NachgebildeterHandler Bei(Func<HttpRequestMessage, bool> passt, Func<HttpRequestMessage, Task<HttpResponseMessage>> antwort)
    {
        _regeln.Insert(0, r => passt(r) ? antwort(r) : null);
        return this;
    }

    public NachgebildeterHandler Bei(HttpMethod methode, string pfad, Func<HttpResponseMessage> antwort) =>
        Bei(r => r.Method == methode && r.RequestUri!.AbsolutePath == pfad, _ => Task.FromResult(antwort()));

    /// <summary>Antworten der Reihe nach; die letzte wiederholt sich.</summary>
    public NachgebildeterHandler BeiDerReihe(HttpMethod methode, string pfad, params Func<HttpResponseMessage>[] antworten)
    {
        var index = 0;
        return Bei(r => r.Method == methode && r.RequestUri!.AbsolutePath == pfad, _ =>
            Task.FromResult(antworten[Math.Min(index++, antworten.Length - 1)]()));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var koerper = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Anfragen.Add(new Aufgezeichnet(
            request.Method,
            request.RequestUri!.PathAndQuery,
            koerper,
            request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value))));
        foreach (var regel in _regeln)
        {
            if (regel(request) is { } antwort)
            {
                var ergebnis = await antwort;
                ergebnis.RequestMessage = request;
                return ergebnis;
            }
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json, string medientyp = "application/json") =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, medientyp) };

    public static HttpResponseMessage Problem(HttpStatusCode status, string code) =>
        Json(status, $$"""{"status":{{(int)status}},"code":"{{code}}","traceId":"00-x"}""", "application/problem+json");

    public HttpClient Client() => new(this, disposeHandler: false) { BaseAddress = new Uri("http://localhost/app/") };
}
