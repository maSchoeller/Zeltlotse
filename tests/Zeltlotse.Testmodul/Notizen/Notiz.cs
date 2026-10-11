using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Mandanten;
using Zeltlotse.Testmodul.Events;

namespace Zeltlotse.Testmodul.Notizen;

internal sealed class Notiz : Aggregat, IOrganisationsEigen
{
    private Notiz()
    {
    }

    public Guid OrganisationId { get; private set; }

    public string Text { get; private set; } = "";

    public static Notiz Anlegen(Guid id, Guid organisationId, string text)
    {
        var notiz = new Notiz { Id = id, OrganisationId = organisationId, Text = text };
        notiz.EreignisAblegen(new NotizAngelegt(id));
        return notiz;
    }

    public void Aendern(string text)
    {
        Text = text;
        EreignisAblegen(new NotizGeaendert(Id));
    }
}
