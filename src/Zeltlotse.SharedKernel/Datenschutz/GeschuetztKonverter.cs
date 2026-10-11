using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Zeltlotse.SharedKernel.Datenschutz;

/// <summary>Bildet <see cref="Geschuetzt"/> auf <c>bytea</c> ab (Konvention in <c>ModulDbContextBasis</c>).</summary>
internal sealed class GeschuetztKonverter() : ValueConverter<Geschuetzt, byte[]>(
    geschuetzt => geschuetzt.AlsBytes(),
    bytes => Geschuetzt.AusBytes(bytes));
