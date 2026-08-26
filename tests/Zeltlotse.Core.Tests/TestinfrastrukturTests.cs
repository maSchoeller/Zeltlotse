namespace Zeltlotse.Core.Tests;

/// <summary>
/// Prüft, dass Testlauf, Assertions und Buildkette funktionieren. Fachliche
/// Tests entstehen je Slice zusammen mit ihrem Code.
/// </summary>
public class TestinfrastrukturTests
{
    private const string Produktname = "Zeltlotse";

    [Fact]
    public void Testlauf_und_Zusicherungen_funktionieren()
    {
        Assert.Equal("Zeltlotse", Produktname);
    }
}
