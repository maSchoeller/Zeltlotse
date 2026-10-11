using Microsoft.EntityFrameworkCore;
using Zeltlotse.SharedKernel.Daten;
using Zeltlotse.SharedKernel.Mandanten;

namespace Zeltlotse.ArchitectureTests.Negativ;

// Negativ-Fixture für AD-27: ein Endpunkt, der den schreibenden DbContext nutzt.
// Wird nie ausgeführt, nur von den Architekturtests als Verstoß erwartet.

internal sealed class NegativDbContext(DbContextOptions<NegativDbContext> optionen, IMandantenKontext kontext)
    : ModulDbContext(optionen, kontext)
{
    protected override string Schema => "negativ";

    protected override void ModellKonfigurieren(ModelBuilder modelBuilder)
    {
    }
}

internal static class FalscherEndpunkt
{
    public static async Task<bool> ErreichbarAsync(NegativDbContext db) =>
        await db.Database.CanConnectAsync();
}
