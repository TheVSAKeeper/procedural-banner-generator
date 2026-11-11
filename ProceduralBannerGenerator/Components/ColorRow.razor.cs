using BannerCore;
using Microsoft.AspNetCore.Components;

namespace ProceduralBannerGenerator.Components;

public sealed partial class ColorRow
{
    private string _hex = "";
    private string? _hexError;
    private string? _lastValue;

    protected override void OnParametersSet()
    {
        if (_lastValue != Value)
        {
            _lastValue = Value;
            _hex = Value;
            _hexError = null;
        }
    }

    private async Task OnHex(ChangeEventArgs e)
    {
        _hex = e.Value?.ToString() ?? "";
        try
        {
            StyleColor.Parse(_hex);
            _hexError = null;
            await ValueChanged.InvokeAsync(_hex.ToUpperInvariant());
        }
        catch (FormatException)
        {
            _hexError = "Введите #RRGGBB или #RRGGBBAA";
        }
    }

    [Parameter]
    [EditorRequired]
    public string Label { get; set; } = "";

    [Parameter]
    public string Value { get; set; } = "#000000";

    [Parameter]
    public EventCallback<string> ValueChanged { get; set; }

    private string Rgb => "#" + Norm(Value)[..6].ToLowerInvariant();

    private int Alpha
    {
        get
        {
            var h = Norm(Value);
            return h.Length == 8 ? Convert.ToInt32(h[6..8], 16) : 255;
        }
    }

    private static string Norm(string v)
    {
        var s = v.TrimStart('#').ToUpperInvariant();
        return s.Length is 6 or 8 ? s : "000000";
    }

    private Task OnColor(ChangeEventArgs e)
    {
        return Emit((e.Value?.ToString() ?? "#000000").TrimStart('#').ToUpperInvariant(), Alpha);
    }

    private Task OnAlpha(ChangeEventArgs e)
    {
        var a = int.TryParse(e.Value?.ToString(), out var x) ? Math.Clamp(x, 0, 255) : 255;
        return Emit(Norm(Value)[..6], a);
    }

    private Task Emit(string rgb, int alpha)
    {
        var hex = alpha >= 255 ? $"#{rgb}" : $"#{rgb}{alpha:X2}";
        return ValueChanged.InvokeAsync(hex);
    }
}
