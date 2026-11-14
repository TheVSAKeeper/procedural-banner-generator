using BannerCore;
using Microsoft.AspNetCore.Components;

namespace ProceduralBannerGenerator.Components;

public sealed partial class StyleCalibrationPanel
{
    private enum StyleTab
    {
        None = 0,
        Palette = 1,
        Layout = 2,
        TitleFx = 3,
        Plate = 4,
        Smear = 5,
        Band = 6,
        Accents = 7,
        Tagline = 8,
        Background = 9,
        Logo = 10,
    }

    private static readonly (StyleTab Tab, string Label)[] Tabs =
    [
        (StyleTab.Palette, "Палитра"),
        (StyleTab.Layout, "Раскладка"),
        (StyleTab.TitleFx, "3D-текст"),
        (StyleTab.Plate, "Плашка"),
        (StyleTab.Smear, "Подмазка"),
        (StyleTab.Band, "Полоса"),
        (StyleTab.Accents, "Акценты"),
        (StyleTab.Tagline, "3-я строка"),
        (StyleTab.Background, "Фон"),
        (StyleTab.Logo, "Лого"),
    ];

    private StyleTab _activeTab = StyleTab.Palette;

    [Parameter]
    [EditorRequired]
    public BannerStyle Style { get; set; } = new();

    [Parameter]
    public EventCallback<BannerStyle> StyleChanged { get; set; }

    private Task Apply(BannerStyle next)
    {
        return StyleChanged.InvokeAsync(next);
    }

    private Task P(Func<BannerStyle.PaletteStyle, BannerStyle.PaletteStyle> f)
    {
        return Apply(
            Style with
            {
                Palette = f(Style.Palette),
            });
    }

    private Task L(Func<BannerStyle.LayoutStyle, BannerStyle.LayoutStyle> f)
    {
        return Apply(
            Style with
            {
                Layout = f(Style.Layout),
            });
    }

    private Task T(Func<BannerStyle.TitleFxStyle, BannerStyle.TitleFxStyle> f)
    {
        return Apply(
            Style with
            {
                TitleFx = f(Style.TitleFx),
            });
    }

    private Task Pl(Func<BannerStyle.PlateStyle, BannerStyle.PlateStyle> f)
    {
        return Apply(
            Style with
            {
                Plate = f(Style.Plate),
            });
    }

    private Task Sm(Func<BannerStyle.SmearStyle, BannerStyle.SmearStyle> f)
    {
        return Apply(
            Style with
            {
                Smear = f(Style.Smear),
            });
    }

    private Task B(Func<BannerStyle.BandStyle, BannerStyle.BandStyle> f)
    {
        return Apply(
            Style with
            {
                Band = f(Style.Band),
            });
    }

    private Task Ac(Func<BannerStyle.AccentsStyle, BannerStyle.AccentsStyle> f)
    {
        return Apply(
            Style with
            {
                Accents = f(Style.Accents),
            });
    }

    private Task Tg(Func<BannerStyle.TaglineStyle, BannerStyle.TaglineStyle> f)
    {
        return Apply(
            Style with
            {
                Tagline = f(Style.Tagline),
            });
    }

    private Task Bg(Func<BannerStyle.BackgroundStyle, BannerStyle.BackgroundStyle> f)
    {
        return Apply(
            Style with
            {
                Background = f(Style.Background),
            });
    }

    private Task Lg(Func<BannerStyle.LogoStyle, BannerStyle.LogoStyle> f)
    {
        return Apply(
            Style with
            {
                Logo = f(Style.Logo),
            });
    }
}
