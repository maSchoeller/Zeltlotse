using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.JSInterop;
using Zeltlotse.Ui.Komponenten;

namespace Zeltlotse.ArchitectureTests.Negativ;

/// <summary>Negativ-Fixture: ein interaktiver Baustein.</summary>
[NurInteraktiv]
internal sealed class InteraktiverBausteinFixture : ComponentBase
{
    [Parameter] public EventCallback Geklickt { get; set; }
}

/// <summary>Negativ-Fixture: eine statische Seite, die einen [NurInteraktiv]-Baustein rendert (wie der Razor-Compiler).</summary>
internal sealed class StatischeSeiteMitInteraktivemBaustein : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<InteraktiverBausteinFixture>(0);
        builder.CloseComponent();
    }
}

/// <summary>Negativ-Fixture: ein Baustein mit EventCallback-Parameter ohne [NurInteraktiv].</summary>
internal sealed class BausteinMitEventCallbackOhneMarke : ComponentBase
{
    [Parameter] public EventCallback<string> Gewaehlt { get; set; }
}

/// <summary>Negativ-Fixture: ein Dienst, der JavaScript aufruft, ohne IBrowser umzusetzen.</summary>
internal sealed class JsNutzerOhneBrowser(IJSRuntime js)
{
    public ValueTask LosAsync() => js.InvokeVoidAsync("alert");
}
