using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using ProceduralBannerGenerator.Models;

namespace ProceduralBannerGenerator.Components;

public sealed partial class BannerStage
{
    [Parameter]
    public string? ImageSrc { get; set; }

    [Parameter]
    public string? ExportHref { get; set; }

    [Parameter]
    public bool IsReady { get; set; }

    [Parameter]
    public string? Error { get; set; }

    [Parameter]
    public string? Notice { get; set; }

    [Parameter]
    public IReadOnlyList<HistoryEntry> History { get; set; } = [];

    [Parameter]
    public EventCallback OnRender { get; set; }

    [Parameter]
    public EventCallback OnRandomize { get; set; }

    [Parameter]
    public EventCallback OnUndoLastStep { get; set; }

    [Parameter]
    public EventCallback OnResetStyle { get; set; }

    [Parameter]
    public EventCallback<InputFileChangeEventArgs> OnImportStyle { get; set; }
}
