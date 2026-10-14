namespace Zeltlotse.SharedKernel.Rechte;

/// <summary>
/// Funktions-Schalter einer Freizeit (AD-20, serverintern). Besitzer ist Freizeiten; Endpunkte erklären ihren Schalter mit
/// <see cref="Rechtemerkmale.GehoertZuSchalter{TBuilder}"/>, Benutzer-Commands mit <see cref="Commands.ISchalterCommand"/>. Nur anhängen.
/// </summary>
public enum FunktionsSchalter : short
{
    Verpflegung = 1,
    Finanzen = 2,
    TeilnehmerZugang = 3,
}
