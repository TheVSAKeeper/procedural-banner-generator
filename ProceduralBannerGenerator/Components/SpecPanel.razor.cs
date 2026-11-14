using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using ProceduralBannerGenerator.Models;

namespace ProceduralBannerGenerator.Components;

public sealed partial class SpecPanel(IJSRuntime js)
{
    private string? _lastTitle;
    private string? _lastSubtitle;
    private string? _lastTagline;
    private WorkspaceMode _lastMode;

    [Parameter]
    public WorkspaceMode Mode { get; set; } = WorkspaceMode.Release;

    [Parameter]
    public bool HasBackground { get; set; }
    [Parameter]
    public string Title { get; set; } = "";

    [Parameter]
    public EventCallback<string> TitleChanged { get; set; }

    [Parameter]
    public string Subtitle { get; set; } = "";

    [Parameter]
    public EventCallback<string> SubtitleChanged { get; set; }

    [Parameter]
    public string Tagline { get; set; } = "";

    [Parameter]
    public EventCallback<string> TaglineChanged { get; set; }

    [Parameter]
    public int Seed { get; set; }

    [Parameter]
    public EventCallback<int> SeedChanged { get; set; }

    [Parameter]
    public int Width { get; set; }

    [Parameter]
    public EventCallback<int> WidthChanged { get; set; }

    [Parameter]
    public int Height { get; set; }

    [Parameter]
    public EventCallback<int> HeightChanged { get; set; }

    [Parameter]
    public int RenderDebounceMs { get; set; }

    [Parameter]
    public EventCallback<int> RenderDebounceMsChanged { get; set; }

    [Parameter]
    public bool Transparent { get; set; }

    [Parameter]
    public EventCallback<bool> TransparentChanged { get; set; }

    [Parameter]
    public EventCallback OnChange { get; set; }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender || _lastTitle != Title || _lastSubtitle != Subtitle || _lastTagline != Tagline || _lastMode != Mode)
        {
            _lastTitle = Title;
            _lastSubtitle = Subtitle;
            _lastTagline = Tagline;
            _lastMode = Mode;
            await js.InvokeVoidAsync("bannerUi.syncTextareas");
        }
    }

    private static string NormalizeLine(string value)
    {
        return value.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
    }

    private async Task OnTitleInput(string value)
    {
        await TitleChanged.InvokeAsync(NormalizeLine(value));
        await OnChange.InvokeAsync();
    }

    private async Task OnSubtitleInput(string value)
    {
        await SubtitleChanged.InvokeAsync(NormalizeLine(value));
        await OnChange.InvokeAsync();
    }

    private async Task OnTaglineInput(string value)
    {
        await TaglineChanged.InvokeAsync(NormalizeLine(value));
        await OnChange.InvokeAsync();
    }

    private async Task OnSeedInput(int value)
    {
        await SeedChanged.InvokeAsync(value);
        await OnChange.InvokeAsync();
    }

    private async Task OnWidthInput(int value)
    {
        await WidthChanged.InvokeAsync(Math.Clamp(value, 320, 3840));
        await OnChange.InvokeAsync();
    }

    private async Task OnHeightInput(int value)
    {
        await HeightChanged.InvokeAsync(Math.Clamp(value, 180, 2160));
        await OnChange.InvokeAsync();
    }

    private async Task ToggleTransparent()
    {
        await TransparentChanged.InvokeAsync(!Transparent);
        await OnChange.InvokeAsync();
    }

    private async Task OnDebounceInput(int value)
    {
        await RenderDebounceMsChanged.InvokeAsync(Math.Clamp(value, 0, 3000));
        await OnChange.InvokeAsync();
    }
}
