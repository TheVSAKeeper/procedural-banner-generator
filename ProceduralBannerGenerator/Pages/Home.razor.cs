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
    private readonly Dictionary<string, (SKImage? Background, SKImage? Logo)> _images = [];
    private SKImage? Background => _images.GetValueOrDefault(ActiveId).Background;
    private SKImage? Logo => _images.GetValueOrDefault(ActiveId).Logo;

    private SKTypeface? _typeface;
    private sealed record PreviewKey(BannerSpec Spec, BannerStyle Style, SKImage? Background, SKImage? Logo);

    private readonly Dictionary<PreviewKey, string> _previews = [];
    private string? _imgSrc;
    private string? _exportHref;
    private string? _error;
    private string? _notice;
    private string? _fontError;
    private bool _loadingFont;
    private bool _disposed;

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

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        _renderDebounce?.Cancel();
        _renderDebounce?.Dispose();
        _promptResetCts?.Cancel();
        _promptResetCts?.Dispose();
        foreach (var image in _images.Values)
        {
            image.Background?.Dispose();
            image.Logo?.Dispose();
        }
        _images.Clear();
        _previews.Clear();
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
        PruneImages();

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

    protected override async Task OnInitializedAsync()
    {
        var loaded = await profiles.LoadAsync(CaptureState());
        ApplyState(loaded);
        _notice = profiles.LoadNotice;
        await mcp.EnsureTokenAsync();
        await LoadFontAsync();

        if (_disposed)
        {
            return;
        }

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

    private async Task LoadFontAsync()
    {
        if (_loadingFont || _disposed)
        {
            return;
        }

        _loadingFont = true;
        _fontError = null;
        try
        {
            var bytes = await http.GetByteArrayAsync("Bender-Black.otf");
            if (_disposed)
            {
                return;
            }

            using var fontData = SKData.CreateCopy(bytes);
            _typeface = SKTypeface.FromData(fontData)
                ?? throw new InvalidOperationException("Не удалось прочитать шрифт");
            _previews.Clear();
            Render();
        }
        catch (Exception ex)
        {
            _fontError = "Не удалось загрузить шрифт баннера: " + ex.Message;
        }
        finally
        {
            _loadingFont = false;
        }
    }

    private void PruneImages()
    {
        foreach (var id in _images.Keys.Where(id => Profiles.All(p => p.Id != id)).ToArray())
        {
            var image = _images[id];
            image.Background?.Dispose();
            image.Logo?.Dispose();
            _images.Remove(id);
        }
    }

    private void DismissNotice()
    {
        _notice = null;
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
        _notice = null;
        _seed = Random.Shared.Next(1, 1_000_000);
        Render();
        await profiles.CommitUiStateAsync(CaptureState(), "Случайный seed");
    }

    private async Task ResetStyle()
    {
        _notice = null;
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
        if (id == ActiveId || Profiles.All(p => p.Id != id))
        {
            return;
        }

        await FlushActiveProfileAsync(render: false);
        var selected = await profiles.SelectProfileAsync(id);
        if (selected is null)
        {
            return;
        }

        _notice = null;
        ApplyState(selected);
        Render();
    }

    private async Task NewProfile()
    {
        await FlushActiveProfileAsync();
        var created = await profiles.CreateNewProfileAsync();
        _notice = null;
        ApplyState(created);
        Render();
    }

    private async Task DuplicateProfile()
    {
        await FlushActiveProfileAsync();
        await profiles.DuplicateActiveProfileAsync();
        _notice = "Профиль скопирован. Фон и логотип загружаются отдельно для каждого профиля.";
        Render();
    }

    private async Task RenameProfile(string name)
    {
        await FlushActiveProfileAsync();
        _notice = null;
        _error = await profiles.RenameActiveProfileAsync(name);
    }

    private async Task DeleteProfile()
    {
        var nextState = await profiles.DeleteActiveProfileAsync();
        if (nextState is null)
        {
            return;
        }

        PruneImages();
        _notice = null;
        ApplyState(nextState);
        Render();
    }

    private async Task FlushActiveProfileAsync(bool render = true)
    {
        _renderDebounce?.Cancel();
        if (await profiles.FlushActiveProfileAsync(CaptureState()) && render)
        {
            Render();
            StateHasChanged();
        }
    }

    private async Task RequestRender()
    {
        _notice = null;
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

        try
        {
            _style.Validate();
            Render();
            await profiles.CommitUiStateAsync(CaptureState());
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
    }

    private async Task UploadBackground(InputFileChangeEventArgs e)
    {
        var profileId = ActiveId;
        try
        {
            var image = await DecodeImage(e.File);
            if (_disposed || Profiles.All(p => p.Id != profileId))
            {
                image.Dispose();
                return;
            }

            var previous = _images.GetValueOrDefault(profileId);
            previous.Background?.Dispose();
            _images[profileId] = (image, previous.Logo);
            _error = null;
            _notice = null;
            Render();
        }
        catch (Exception ex)
        {
            _error = "Фон не удалось загрузить: " + ex.Message;
        }
    }

    private async Task UploadLogo(InputFileChangeEventArgs e)
    {
        var profileId = ActiveId;
        try
        {
            var image = await DecodeImage(e.File);
            if (_disposed || Profiles.All(p => p.Id != profileId))
            {
                image.Dispose();
                return;
            }

            var previous = _images.GetValueOrDefault(profileId);
            previous.Logo?.Dispose();
            _images[profileId] = (previous.Background, image);
            if (profileId != ActiveId)
            {
                var profile = Profiles.First(p => p.Id == profileId);
                var next = profile.State with { Style = profile.State.Style with { Logo = profile.State.Style.Logo with { Enabled = true } } };
                if (BannerProfileService.HasTemplateChanged(profile.State, next))
                {
                    BannerProfileService.PushHistory(profile, "Загрузка логотипа", profile.State);
                    profile.State = next;
                    await profiles.PersistAsync();
                }

                return;
            }

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
            _notice = null;
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
        Background?.Dispose();
        _images[ActiveId] = (null, Logo);
        _notice = null;
        Render();
    }

    private async Task ClearLogo()
    {
        Logo?.Dispose();
        _images[ActiveId] = (Background, null);
        _style = _style with { Logo = _style.Logo with { Enabled = false } };
        _notice = null;
        Render();
        await profiles.CommitUiStateAsync(CaptureState(), "Удаление логотипа");
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
            _imgSrc = "data:image/png;base64," + RenderStateToBase64(CaptureState(), ActiveId);
            _error = null;
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
    }

    private string? RenderStateToBase64(SavedState s, string profileId)
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

        var images = _images.GetValueOrDefault(profileId);
        var key = new PreviewKey(spec, s.Style, images.Background, images.Logo);
        if (_previews.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var png = new BannerRenderer(s.Style).RenderPng(spec, _typeface, images.Background, images.Logo);
        var base64 = Convert.ToBase64String(png);
        const int maxEntries = 8;
        const int maxChars = 8 * 1024 * 1024;
        if (base64.Length <= maxChars)
        {
            while (_previews.Count >= maxEntries || _previews.Values.Sum(p => p.Length) + base64.Length > maxChars)
            {
                _previews.Remove(_previews.Keys.First());
            }

            _previews[key] = base64;
        }

        return base64;
    }

    private async Task ImportStyle(InputFileChangeEventArgs e)
    {
        try
        {
            await using var stream = e.File.OpenReadStream(1024 * 1024);
            using var reader = new StreamReader(stream);
            _style = BannerStyle.FromJson(await reader.ReadToEndAsync());
            _error = null;
            _notice = null;
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
