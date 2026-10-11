namespace Zeltlotse.Personen.Contracts.Events;

/// <summary>
/// Ein volljähriges Kind hat sein Profil übernommen (FR-4, AD-7, Tabelle Ereignis-Fluss): Das Konto <paramref name="KontoId"/>
/// gehört ab jetzt zur Person <paramref name="PersonId"/>. Empfänger ist Konten (Konto mit dem Profil verbinden).
/// Datensatz im Umschlag ist die Profil-Übergabe.
/// </summary>
public sealed record ProfilUebergabeAngenommen(Guid PersonId, Guid KontoId);
