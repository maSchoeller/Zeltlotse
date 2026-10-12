using Mediator;
using Zeltlotse.Personen.Daten;
using Zeltlotse.SharedKernel.Commands;
using Zeltlotse.SharedKernel.Dateien;

namespace Zeltlotse.Personen.Nachweise;

/// <summary>
/// Legt die Datei eines eigenen Nachweises verschlüsselt im Dateispeicher ab (FR-8, AD-14, AD-15) und ersetzt eine frühere
/// unter derselben Kennung. Die Prüfung am Inhalt (Typ, Größe) hat der Endpunkt erledigt. Ein fremder oder unbekannter Nachweis
/// ist <see cref="NachweisDateiErgebnis.NichtGefunden"/> (<c>404</c>); während einer laufenden Prüfung
/// <see cref="NachweisDateiErgebnis.InPruefung"/> (<c>409</c>).
/// </summary>
internal sealed record NachweisDateiHochladen(Guid PersonId, Guid NachweisId, byte[] Inhalt)
    : ICommand<NachweisDateiErgebnis>, IModulCommand<PersonenDbContext>, IBenutzerCommand
{
    /// <summary>Nie den Inhalt ausgeben (AD-22).</summary>
    public override string ToString() => $"{nameof(NachweisDateiHochladen)}({NachweisId})";
}

/// <summary>Ergebnis des Hochladens.</summary>
internal abstract record NachweisDateiErgebnis
{
    private NachweisDateiErgebnis()
    {
    }

    public sealed record Hochgeladen(DateTimeOffset Am) : NachweisDateiErgebnis;

    public sealed record NichtGefunden : NachweisDateiErgebnis;

    /// <summary>Gerade zur Prüfung freigegeben: <c>409</c>.</summary>
    public sealed record InPruefung : NachweisDateiErgebnis;
}

internal sealed class NachweisDateiHochladenHandler(PersonenDbContext db, IGeschuetzteDateien dateien, TimeProvider uhr)
    : ICommandHandler<NachweisDateiHochladen, NachweisDateiErgebnis>
{
    public async ValueTask<NachweisDateiErgebnis> Handle(NachweisDateiHochladen command, CancellationToken cancellationToken)
    {
        var nachweis = await NachweisSperre.EigenerAsync(db.Nachweise, command.PersonId, command.NachweisId, cancellationToken);
        if (nachweis is null)
        {
            return new NachweisDateiErgebnis.NichtGefunden();
        }

        var jetzt = uhr.GetUtcNow();
        if (await NachweisSperre.InPruefungAsync(db.EinsichtPosten, db.Einsichten, nachweis.Id, jetzt, cancellationToken))
        {
            return new NachweisDateiErgebnis.InPruefung();
        }

        await dateien.SpeichernAsync(Nachweis.DateiPraefix, nachweis.Id, nachweis.PersonId, command.Inhalt, cancellationToken);
        nachweis.DateiHochgeladen(jetzt);
        return new NachweisDateiErgebnis.Hochgeladen(jetzt);
    }
}
