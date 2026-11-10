using Microsoft.AspNetCore.Components;
using ProceduralBannerGenerator.Models;

namespace ProceduralBannerGenerator.Components;

public sealed partial class ProfilePanel
{
    [Parameter]
    public IReadOnlyList<ProfileEntry> Profiles { get; set; } = [];

    [Parameter]
    public string ActiveId { get; set; } = "";

    [Parameter]
    public IReadOnlyList<HistoryEntry> History { get; set; } = [];

    [Parameter]
    public bool McpConnected { get; set; }

    [Parameter]
    public bool PromptCopied { get; set; }

    [Parameter]
    public string AgentPrompt { get; set; } = "";

    [Parameter]
    public EventCallback<string> OnSelectProfile { get; set; }

    [Parameter]
    public EventCallback OnNewProfile { get; set; }

    [Parameter]
    public EventCallback OnRenameProfile { get; set; }

    [Parameter]
    public EventCallback OnDuplicateProfile { get; set; }

    [Parameter]
    public EventCallback OnDeleteProfile { get; set; }

    [Parameter]
    public EventCallback OnCopyAgentPrompt { get; set; }

    [Parameter]
    public EventCallback<int> OnUndoSteps { get; set; }

    private Task HandleProfileSelected(ChangeEventArgs e)
    {
        var id = e.Value?.ToString();
        if (string.IsNullOrEmpty(id) || id == ActiveId)
        {
            return Task.CompletedTask;
        }

        return OnSelectProfile.InvokeAsync(id);
    }
}
