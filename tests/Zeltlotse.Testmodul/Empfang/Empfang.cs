using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Ereignisse;

namespace Zeltlotse.Testmodul.Empfang;

/// <summary>Hält fest, welcher Empfänger welches Ereignis mit welchem Mandanten-Kontext verarbeitet hat.</summary>
internal sealed class Empfang
{
    public Guid Id { get; init; }

    public required string Empfaenger { get; init; }

    public Guid EreignisId { get; init; }

    public Guid NotizId { get; init; }

    public long Folgenummer { get; init; }

    public Guid? KontextOrganisationId { get; init; }

    public static Empfang Neu(string empfaenger, Umschlag umschlag, Guid notizId, Guid? kontextOrganisationId, TimeProvider uhr) => new()
    {
        Id = Kennung.Neu(uhr),
        Empfaenger = empfaenger,
        EreignisId = umschlag.EreignisId,
        NotizId = notizId,
        Folgenummer = umschlag.Folgenummer,
        KontextOrganisationId = kontextOrganisationId,
    };
}
