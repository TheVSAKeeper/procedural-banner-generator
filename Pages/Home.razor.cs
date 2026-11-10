using BannerCore;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using ProceduralBannerGenerator.Models;
using ProceduralBannerGenerator.Services;
using SkiaSharp;

namespace ProceduralBannerGenerator.Pages;

public sealed partial class Home(
    HttpClient http,
    IJSRuntime js,
    BannerProfileService profiles,
    BannerMcpService mcp) : IAsyncDisposable
{
    private string _title = "290. ЛЕДОКОЛ XIX";
    private string _subtitle = "КОРОЛЬ МАШИННОГО ЗАЛА";
    private string _tagline = "";
    private int _seed = 290;
    private int _width = 1280;
    private int _height = 720;
    private bool _transparent;

    private BannerStyle _style = new();

    // TODO: картинки фона/лого держим только в памяти – в localStorage не кладём (квота ~5 МБ, фон легко её пробьёт). После перезагрузки – загрузить заново.
    private SKImage? _background;
    private SKImage? _logo;

    private SKTypeface? _typeface;
    private string? _imgSrc;
    private string? _exportHref;
    private string? _error;
    private string? _notice;

    private bool _mcpConnected;
    private bool _promptCopied;
    private DotNetObjectReference<Home>? _dotNetRef;

    private int _renderDebounceMs = 150;
    private CancellationTokenSource? _renderDebounce;
    private CancellationTokenSource? _promptResetCts;

    private IReadOnlyList<ProfileEntry> Profiles => profiles.Profiles;

    private string ActiveId => profiles.ActiveId;

    private IReadOnlyList<HistoryEntry> ActiveHistory => profiles.ActiveHistory;

    private string AgentPrompt => mcp.BuildAgentPrompt();

    protected override async Task OnInitializedAsync()
    {
        var bytes = await http.GetByteArrayAsync("Bender-Black.otf");
        using var fontData = SKData.CreateCopy(bytes);
        _typeface = SKTypeface.FromData(fontData);

        var loaded = await profiles.LoadAsync(CaptureState());
        ApplyState(loaded);
        await mcp.EnsureTokenAsync();
        Render();

        _dotNetRef = DotNetObjectReference.Create(this);
        try
        {
            var wsUrl = $"ws://127.0.0.1:5219/ws?token={Uri.EscapeDataString(mcp.Token)}";
            await js.InvokeVoidAsync("bannerMcp.bind", _dotNetRef, wsUrl, mcp.Token);
        }
        catch
        {
            // Мост опционален – при ошибке JS-интеропа редактор продолжает работать автономно.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _renderDebounce?.Cancel();
        _renderDebounce?.Dispose();
        _promptResetCts?.Cancel();
        _promptResetCts?.Dispose();
        _background?.Dispose();
        _logo?.Dispose();
        _typeface?.Dispose();

        try
        {
            await js.InvokeVoidAsync("bannerMcp.unbind");
        }
        catch (JSDisconnectedException)
        {
        }
        catch (ObjectDisposedException)
        {
        }

        _dotNetRef?.Dispose();
    }

    [JSInvokable]
    public Task OnMcpConnectionChanged(bool connected)
    {
        return InvokeAsync(() =>
        {
            _mcpConnected = connected;
            StateHasChanged();
        });
    }

    [JSInvokable]
    public async Task<string> InvokeMcpToolAsync(string tool, string argsJson)
    {
        await FlushActiveProfileAsync();
        var result = await mcp.InvokeToolAsync(tool, argsJson, _renderDebounceMs, RenderStateToBase64);

        if (result.ActiveStateToApply is { } nextState)
        {
            ApplyState(nextState);
            Render();
        }

        if (result.Error is not null)
        {
            _error = result.Error;
        }
        else if (result.Notice is not null)
        {
            _error = null;
            _notice = result.Notice;
        }

        await InvokeAsync(StateHasChanged);
        return result.ResponseJson;
    }

    private async Task CopyAgentPrompt()
    {
        _promptResetCts?.Cancel();
        _promptResetCts?.Dispose();
        var cts = new CancellationTokenSource();
        _promptResetCts = cts;

        try
        {
            await mcp.CopyAgentPromptAsync();
            _promptCopied = true;
            _notice = "Промпт для агента скопирован – вставьте его в чат агента";
            StateHasChanged();
            await Task.Delay(2200, cts.Token);
            _promptCopied = false;
        }
        catch (TaskCanceledException)
        {
        }
        catch (Exception ex)
        {
            _error = "Не удалось скопировать промпт: " + ex.Message;
        }
    }

    private async Task Randomize()
    {
        _seed = Random.Shared.Next(1, 1_000_000);
        Render();
        await profiles.CommitUiStateAsync(CaptureState(), "Случайный seed");
    }

    private async Task ResetStyle()
    {
        _style = new();
        Render();
        await profiles.CommitUiStateAsync(CaptureState(), "Сброс стиля");
    }

    private Task UndoLastStepAsync()
    {
        return UndoStepsAsync(1);
    }

    private async Task UndoStepsAsync(int steps)
    {
        await FlushActiveProfileAsync();
        var undone = await profiles.UndoStepsAsync(steps);
        if (undone is null)
        {
            return;
        }

        ApplyState(undone.Value.State);
        _error = null;
        _notice = undone.Value.Notice;
        Render();
    }

    private async Task SelectProfileAsync(string id)
    {
        await FlushActiveProfileAsync();
        var selected = await profiles.SelectProfileAsync(id);
        if (selected is null)
        {
            return;
        }

        ApplyState(selected);
        Render();
    }

    private async Task NewProfile()
    {
        await FlushActiveProfileAsync();
        var created = await profiles.CreateNewProfileAsync();
        ApplyState(created);
        Render();
    }

    private async Task DuplicateProfile()
    {
        await FlushActiveProfileAsync();
        await profiles.DuplicateActiveProfileAsync();
        Render();
    }

    private async Task RenameProfile()
    {
        await FlushActiveProfileAsync();
        _error = await profiles.PromptAndRenameActiveProfileAsync();
    }

    private async Task DeleteProfile()
    {
        var nextState = await profiles.DeleteActiveProfileAsync();
        if (nextState is null)
        {
            return;
        }

        ApplyState(nextState);
        Render();
    }

    private async Task FlushActiveProfileAsync()
    {
        _renderDebounce?.Cancel();
        if (await profiles.FlushActiveProfileAsync(CaptureState()))
        {
            Render();
            StateHasChanged();
        }
    }

    private async Task RequestRender()
    {
        _renderDebounce?.Cancel();
        _renderDebounce?.Dispose();
        var cts = new CancellationTokenSource();
        _renderDebounce = cts;

        try
        {
            await Task.Delay(Math.Max(0, _renderDebounceMs), cts.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        Render();
        await profiles.CommitUiStateAsync(CaptureState());
    }

    private async Task UploadBackground(InputFileChangeEventArgs e)
    {
        try
        {
            _background?.Dispose();
            _background = await DecodeImage(e.File);
            _error = null;
            Render();
            await profiles.CommitUiStateAsync(CaptureState());
        }
        catch (Exception ex)
        {
            _error = "Фон не удалось загрузить: " + ex.Message;
        }
    }

    private async Task UploadLogo(InputFileChangeEventArgs e)
    {
        try
        {
            _logo?.Dispose();
            _logo = await DecodeImage(e.File);
            if (!_style.Logo.Enabled)
            {
                _style = _style with
                {
                    Logo = _style.Logo with
                    {
                        Enabled = true,
                    },
                };
            }

            _error = null;
            Render();
            await profiles.CommitUiStateAsync(CaptureState(), "Загрузка логотипа");
        }
        catch (Exception ex)
        {
            _error = "Лого не удалось загрузить: " + ex.Message;
        }
    }

    private void ClearBackground()
    {
        _background?.Dispose();
        _background = null;
        Render();
    }

    private void ClearLogo()
    {
        _logo?.Dispose();
        _logo = null;
        Render();
    }

    private static async Task<SKImage> DecodeImage(IBrowserFile file)
    {
        await using var stream = file.OpenReadStream(20 * 1024 * 1024);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        using var data = SKData.CreateCopy(ms.ToArray());
        using var codec = SKCodec.Create(data)
            ?? throw new InvalidOperationException("формат картинки не распознан");

        if (codec.Info.Width <= 0
            || codec.Info.Height <= 0
            || codec.Info.Width > 8192
            || codec.Info.Height > 8192
            || ((long)codec.Info.Width * codec.Info.Height) > 33_554_432)
        {
            throw new InvalidOperationException($"размер изображения ({codec.Info.Width}x{codec.Info.Height}) превышает допустимый лимит");
        }

        return SKImage.FromEncodedData(data)
            ?? throw new InvalidOperationException("формат картинки не распознан");
    }

    private void Render()
    {
        if (_typeface is null)
        {
            return;
        }

        _exportHref = "data:application/json;charset=utf-8," + Uri.EscapeDataString(_style.ToJson());

        try
        {
            var spec = new BannerSpec
            {
                Title = string.IsNullOrWhiteSpace(_title) ? " " : _title,
                Subtitle = _subtitle ?? "",
                Tagline = string.IsNullOrWhiteSpace(_tagline) ? null : _tagline,
                Seed = _seed,
                Width = Math.Clamp(_width, 320, 3840),
                Height = Math.Clamp(_height, 180, 2160),
                Transparent = _transparent,
            };

            var png = new BannerRenderer(_style).RenderPng(spec, _typeface, _background, _logo);
            _imgSrc = "data:image/png;base64," + Convert.ToBase64String(png);
            _error = null;
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
    }

    private string? RenderStateToBase64(SavedState s)
    {
        if (_typeface is null)
        {
            return null;
        }

        var spec = new BannerSpec
        {
            Title = string.IsNullOrWhiteSpace(s.Title) ? " " : s.Title,
            Subtitle = s.Subtitle ?? "",
            Tagline = string.IsNullOrWhiteSpace(s.Tagline) ? null : s.Tagline,
            Seed = s.Seed,
            Width = Math.Clamp(s.Width, 320, 3840),
            Height = Math.Clamp(s.Height, 180, 2160),
            Transparent = s.Transparent,
        };

        var png = new BannerRenderer(s.Style).RenderPng(spec, _typeface, _background, _logo);
        return Convert.ToBase64String(png);
    }

    private async Task ImportStyle(InputFileChangeEventArgs e)
    {
        try
        {
            await using var stream = e.File.OpenReadStream(1024 * 1024);
            using var reader = new StreamReader(stream);
            _style = BannerStyle.FromJson(await reader.ReadToEndAsync());
            _error = null;
            Render();
            await profiles.CommitUiStateAsync(CaptureState(), "Импорт стиля");
        }
        catch (Exception ex)
        {
            _error = "Импорт стиля не удался: " + ex.Message;
        }
    }

    private SavedState CaptureState()
    {
        return new()
        {
            Title = _title,
            Subtitle = _subtitle,
            Tagline = _tagline,
            Seed = _seed,
            Width = _width,
            Height = _height,
            Transparent = _transparent,
            RenderDebounceMs = _renderDebounceMs,
            Style = _style,
        };
    }

    private void ApplyState(SavedState s)
    {
        _title = s.Title;
        _subtitle = s.Subtitle;
        _tagline = s.Tagline ?? "";
        _seed = s.Seed;
        _width = s.Width;
        _height = s.Height;
        _transparent = s.Transparent;
        _renderDebounceMs = s.RenderDebounceMs;
        _style = s.Style;
    }
}
