using System.Reflection;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Zeltlotse.Testing;

/// <summary>
/// Gibt die Zugriffsbegrenzer der Middleware-Pipeline frei, wenn eine <see cref="ZeltlotseApiFactory"/> endet.
/// Die Middleware von <c>UseRateLimiter</c> legt einen Begrenzer mit eigenem Zeitgeber (alle 100 ms) an und gibt ihn nie frei;
/// über Zeitgeber, Middleware und Logger hielte er den ganzen beendeten Host am Leben. Im Betrieb lebt der Host so lange wie der
/// Prozess, in den Tests entstünde je Factory ein Leck (mehrere MB und ein weiterer Zeitgeber), das den Testlauf mit der Zeit
/// immer langsamer macht.
/// </summary>
internal sealed class ZugriffsbegrenzerAufraeumen : IStartupFilter
{
    private readonly List<RequestDelegate> _pipelines = [];

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        // Als erstes Glied angemeldet, wird diese Fabrik beim Bau der Pipeline zuletzt aufgerufen und sieht den Rest der Pipeline.
        app.Use(weiter =>
        {
            lock (_pipelines)
            {
                _pipelines.Add(weiter);
            }

            return weiter;
        });
        next(app);
    };

    /// <summary>Gibt jeden Begrenzer frei, der in der Kette der Middleware-Delegaten hängt (nach dem Ende des Hosts aufrufen).</summary>
    public void Freigeben()
    {
        List<RequestDelegate> pipelines;
        lock (_pipelines)
        {
            pipelines = [.. _pipelines];
            _pipelines.Clear();
        }

        var gesehen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var offen = new Queue<object>(pipelines);
        while (offen.TryDequeue(out var objekt))
        {
            if (!gesehen.Add(objekt))
            {
                continue;
            }

            if (objekt is Delegate delegat)
            {
                if (delegat.Target is { } ziel)
                {
                    offen.Enqueue(ziel);
                }

                continue;
            }

            // Middleware-Instanzen und Closures: nur Delegaten weiterverfolgen (die nächste Middleware), Begrenzer freigeben.
            for (var typ = objekt.GetType(); typ is not null && typ != typeof(object); typ = typ.BaseType)
            {
                foreach (var feld in typ.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    switch (feld.GetValue(objekt))
                    {
                        case Delegate naechster:
                            offen.Enqueue(naechster);
                            break;
                        case PartitionedRateLimiter<HttpContext> begrenzer:
                            begrenzer.Dispose();
                            break;
                    }
                }
            }
        }
    }
}
