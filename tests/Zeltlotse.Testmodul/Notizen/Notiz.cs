using Zeltlotse.SharedKernel;
using Zeltlotse.SharedKernel.Datenschutz;
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

    /// <summary>Besonders schützenswert (AD-14): nur als Chiffretext der zentralen Verschlüsselung.</summary>
    [Besonders]
    public Geschuetzt? Vertraulich { get; private set; }

    public static Notiz Anlegen(Guid id, Guid organisationId, string text)
    {
        var notiz = new Notiz { Id = id, OrganisationId = organisationId, Text = text };
        notiz.EreignisAblegen(new NotizAngelegt(id));
        return notiz;
    }

    public void VertraulichSetzen(Geschuetzt vertraulich) => Vertraulich = vertraulich;

    public void Aendern(string text)
    {
        Text = text;
        EreignisAblegen(new NotizGeaendert(Id));
    }
}
