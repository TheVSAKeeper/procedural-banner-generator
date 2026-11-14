using Microsoft.AspNetCore.Components;
using ProceduralBannerGenerator.Models;

namespace ProceduralBannerGenerator.Components;

public sealed partial class HistoryPanel
{
    [Parameter]
    public IReadOnlyList<HistoryEntry> History { get; set; } = [];

    [Parameter]
    public bool Collapsible { get; set; }

    [Parameter]
    public EventCallback<int> OnUndoSteps { get; set; }
}
