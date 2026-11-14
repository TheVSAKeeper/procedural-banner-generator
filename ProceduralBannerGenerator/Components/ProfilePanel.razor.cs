using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using ProceduralBannerGenerator.Models;

namespace ProceduralBannerGenerator.Components;

public sealed partial class ProfilePanel
{
    private static readonly (WorkspaceMode Mode, string Label, string Icon)[] Modes =
    [
        (WorkspaceMode.Release, "Выпуск", "file-text"),
        (WorkspaceMode.Calibration, "Калибровка", "sliders"),
        (WorkspaceMode.Agent, "Агент и MCP", "terminal"),
        (WorkspaceMode.Focus, "Фокус", "maximize"),
    ];

    private bool _manageOpen;
    private bool _renaming;
    private bool _deleting;
    private bool _focusName;
    private string _name = "";
    private string _editingId = "";
    private ElementReference _nameInput;

    [Parameter]
    public WorkspaceMode ActiveMode { get; set; } = WorkspaceMode.Release;

    [Parameter]
    public EventCallback<WorkspaceMode> OnSelectMode { get; set; }

    [Parameter]
    public bool PreserveTextOnSwitch { get; set; }

    [Parameter]
    public EventCallback<bool> PreserveTextOnSwitchChanged { get; set; }

    [Parameter]
    public IReadOnlyList<ProfileEntry> Profiles { get; set; } = [];

    [Parameter]
    public string ActiveId { get; set; } = "";

    [Parameter]
    public bool McpConnected { get; set; }

    [Parameter]
    public EventCallback<string> OnSelectProfile { get; set; }

    [Parameter]
    public EventCallback OnNewProfile { get; set; }

    [Parameter]
    public EventCallback<string> OnRenameProfile { get; set; }

    [Parameter]
    public EventCallback OnDuplicateProfile { get; set; }

    [Parameter]
    public EventCallback OnDeleteProfile { get; set; }

    protected override void OnParametersSet()
    {
        if (_editingId != ActiveId)
        {
            CancelEdit();
            _editingId = ActiveId;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusName)
        {
            _focusName = false;
            await _nameInput.FocusAsync();
        }
    }

    private void ToggleManage()
    {
        _manageOpen = !_manageOpen;
        if (!_manageOpen)
        {
            CancelEdit();
        }
    }

    private void BeginRename()
    {
        _deleting = false;
        _name = Profiles.FirstOrDefault(p => p.Id == ActiveId)?.Name ?? "";
        _renaming = true;
        _focusName = true;
    }

    private void BeginDelete()
    {
        CancelEdit();
        _deleting = true;
    }

    private void CancelEdit()
    {
        _renaming = false;
        _deleting = false;
        _focusName = false;
    }

    private async Task SaveName()
    {
        if (string.IsNullOrWhiteSpace(_name))
        {
            return;
        }

        await OnRenameProfile.InvokeAsync(_name);
        _renaming = false;
    }

    private async Task ConfirmDelete()
    {
        _deleting = false;
        await OnDeleteProfile.InvokeAsync();
    }

    private Task HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
        {
            if (_renaming || _deleting)
            {
                CancelEdit();
            }
            else
            {
                _manageOpen = false;
            }
        }
        else if (e.Key == "Enter" && _renaming)
        {
            return SaveName();
        }

        return Task.CompletedTask;
    }

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
