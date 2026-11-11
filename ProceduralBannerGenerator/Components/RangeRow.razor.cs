using Microsoft.AspNetCore.Components;
using System.Globalization;

namespace ProceduralBannerGenerator.Components;

public sealed partial class RangeRow
{
    [Parameter]
    [EditorRequired]
    public string Label { get; set; } = "";

    [Parameter]
    public float Value { get; set; }

    [Parameter]
    public EventCallback<float> ValueChanged { get; set; }

    [Parameter]
    public float Min { get; set; }

    [Parameter]
    public float Max { get; set; } = 1f;

    [Parameter]
    public float Step { get; set; } = 0.01f;

    private static string Fmt(float v)
    {
        return v.ToString(CultureInfo.InvariantCulture);
    }

    private Task OnInput(ChangeEventArgs e)
    {
        if (float.TryParse(e.Value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && float.IsFinite(v))
        {
            return ValueChanged.InvokeAsync(Math.Clamp(v, Min, Max));
        }

        return Task.CompletedTask;
    }
}
