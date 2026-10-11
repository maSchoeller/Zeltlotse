using Mediator;
using Microsoft.EntityFrameworkCore;
using Zeltlotse.Personen.Daten;
using Zeltlotse.Personen.Schluessel;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Datenschutz;

namespace Zeltlotse.Personen.Profil;

/// <summary>
/// Legt die Person der Sitzung an, falls sie fehlt, und in derselben Transaktion ihren Personen-Schlüssel (AD-14).
/// Idempotent: Eine vorhandene Person bleibt unverändert. Das Ergebnis ist die verfolgte Person; nach dem Speichern trägt
/// sie ihre Version.
/// </summary>
internal sealed record ProfilSicherstellen(Guid PersonId) : ICommand<Person>, IModulCommand<PersonenDbContext>, IBenutzerCommand;

internal sealed class ProfilSicherstellenHandler(PersonenDbContext db, Hauptschluessel hauptschluessel, TimeProvider uhr)
    : ICommandHandler<ProfilSicherstellen, Person>
{
    public async ValueTask<Person> Handle(ProfilSicherstellen command, CancellationToken cancellationToken)
    {
        var person = await db.Personen.SingleOrDefaultAsync(p => p.Id == command.PersonId, cancellationToken);
        if (person is not null)
        {
            return person;
        }

        person = Person.Anlegen(command.PersonId, uhr.GetUtcNow());
        db.Personen.Add(person);
        if (!await db.PersonenSchluessel.AnyAsync(s => s.PersonId == command.PersonId, cancellationToken))
        {
            db.PersonenSchluessel.Add(PersonenSchluessel.Anlegen(
                command.PersonId, hauptschluessel.NeuenPersonenSchluesselUmhuellen(command.PersonId), uhr));
        }

        return person;
    }
}
