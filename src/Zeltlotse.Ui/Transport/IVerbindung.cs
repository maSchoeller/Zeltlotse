namespace Zeltlotse.Ui.Transport;

/// <summary>Empfänger der Verbindungsmeldungen des <see cref="VerbindungsHandler"/>; umgesetzt von der Web-App (AD-18).</summary>
public interface IVerbindung
{
    /// <summary>Ein API-Aufruf endete mit einem Netzfehler (keine Antwort vom Server).</summary>
    ValueTask NetzfehlerMeldenAsync();

    /// <summary>Ein API-Aufruf bekam eine Antwort vom Server (die Verbindung steht).</summary>
    ValueTask ErfolgMeldenAsync();
}
