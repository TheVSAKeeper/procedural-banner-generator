using System.Text.RegularExpressions;
using SkiaSharp;

namespace ProceduralBannerGenerator;

/// <summary>Рендер баннера по структуре референса (превью Tarkov-стрима):
/// чёрная плашка-мазок → зелёная подмазка за номером → зелёная полоса-мазок с «зубьями» →
/// номер зелёным 3D + заголовок белым 3D с мотлингом → чёрный подзаголовок на зелёной полосе.
/// Все ручки — в <see cref="BannerStyle"/>; рендер детерминирован по seed.
// ponytail: резервная веб-версия без загрузки фона/лого — добавить, если понадобится upload.</summary>
public sealed partial class BannerRenderer(BannerStyle style)
{
    [GeneratedRegex(@"^(\d+[.:]?)\s+(.+)$")]
    private static partial Regex TitlePrefixRegex();

    /// <summary>Разбор заголовка: ведущий номер («290.» / «290:» / «290») отделяется от текста —
    /// он рендерится зелёным 3D. Вынесено internal как чистая функция ради юнит-теста.</summary>
    internal static (string? Prefix, string Main) SplitTitle(string title)
    {
        var match = TitlePrefixRegex().Match(title);
        return match.Success ? (match.Groups[1].Value, match.Groups[2].Value) : (null, title);
    }

    public byte[] RenderPng(BannerSpec spec, SKTypeface typeface)
    {
        var info = new SKImageInfo(spec.Width, spec.Height);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        var rng = new Random(spec.Seed);
        var layout = style.Layout;

        if (spec.Transparent)
            canvas.Clear(SKColors.Transparent);
        else
            DrawPlaceholderBackground(canvas, spec.Width, spec.Height);

        // --- разбор заголовка: ведущий номер («290.») в оригинале зелёный ---
        var (prefix, main) = SplitTitle(spec.Title);

        // --- шрифты и автоподгонка ---
        var titleSize = spec.Width * Math.Clamp(layout.TitleSizeFraction, 0.02f, 0.15f);
        using var titleFont = new SKFont(typeface, titleSize) { ScaleX = layout.TitleScaleX, Subpixel = true };
        var maxTitleWidth = spec.Width * layout.MaxTitleWidthFraction;
        var totalWidth = MeasureTitleRow(titleFont, prefix, main, out var spaceWidth);

        if (totalWidth > maxTitleWidth)
        {
            titleSize *= maxTitleWidth / totalWidth;
            titleFont.Size = titleSize;
            totalWidth = MeasureTitleRow(titleFont, prefix, main, out spaceWidth);
        }

        using var subFont = new SKFont(typeface, titleSize * layout.SubtitleSizeFactor)
        {
            ScaleX = layout.SubtitleScaleX,
            Subpixel = true,
        };
        var subWidth = subFont.MeasureText(spec.Subtitle, out var subBounds);

        // --- раскладка ---
        var centerX = spec.Width / 2f;
        var titleBaseline = spec.Height * layout.TitleBaselineFraction;
        var subBaseline = titleBaseline + titleSize * layout.SubtitleBaselineOffsetFactor;
        var titleLeft = centerX - totalWidth / 2;
        titleFont.MeasureText(main, out var mainBounds);

        var plate = new SKRect(
            titleLeft - titleSize * layout.PlateSidePaddingFactor,
            titleBaseline + mainBounds.Top - titleSize * layout.PlateTopPaddingFactor,
            titleLeft + totalWidth + titleSize * layout.PlateSidePaddingFactor,
            titleBaseline + titleSize * layout.PlateBottomFactor);

        var bandHeight = subFont.Size * layout.BandHeightFactor;
        var bandCenterY = subBaseline + (subBounds.Top + subBounds.Bottom) / 2;
        var band = new SKRect(
            centerX - subWidth / 2 - subFont.Size * layout.BandLeftPaddingFactor,
            bandCenterY - bandHeight / 2,
            centerX + subWidth / 2 + subFont.Size * layout.BandRightPaddingFactor,
            bandCenterY + bandHeight / 2);

        // --- весь блок чуть наклонён ---
        canvas.Save();
        canvas.RotateDegrees(Math.Clamp(layout.RotationDegrees, -10f, 10f), centerX, plate.MidY);

        DrawBlackPlate(canvas, plate, spec.Seed, rng);

        if (prefix is not null && style.Smear.Enabled)
        {
            var prefixWidth = titleFont.MeasureText(prefix);
            DrawGreenSmear(canvas, new SKRect(
                plate.Left - titleSize * style.Smear.LeftExtendFactor,
                plate.Top - titleSize * style.Smear.TopExtendFactor,
                titleLeft + prefixWidth + titleSize * style.Smear.RightPadFactor,
                titleBaseline + titleSize * style.Smear.BaselineDropFactor), spec.Seed, rng);
        }

        DrawChipsAndSplatter(canvas, plate, rng);
        DrawGreenBand(canvas, band, spec.Seed, rng);
        DrawTitleRow(canvas, prefix, main, titleFont, titleLeft, titleBaseline, spaceWidth, titleSize, rng);
        DrawSubtitle(canvas, spec.Subtitle, subFont, centerX, subBaseline, subWidth, subBounds, rng);

        if (!string.IsNullOrWhiteSpace(spec.Tagline) && style.Tagline.Enabled)
            DrawTagline(canvas, spec.Tagline!, typeface, centerX, subBaseline, titleSize, rng);

        canvas.Restore();

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static float MeasureTitleRow(SKFont font, string? prefix, string main, out float spaceWidth)
    {
        spaceWidth = prefix is null ? 0 : font.MeasureText(" ") * 1.6f;
        var prefixWidth = prefix is null ? 0 : font.MeasureText(prefix);
        return prefixWidth + spaceWidth + font.MeasureText(main);
    }

    /// <summary>Тёмная подложка-заглушка вместо скриншота игры — оценить контраст баннера.</summary>
    private static void DrawPlaceholderBackground(SKCanvas canvas, int width, int height)
    {
        using var paint = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(0, height),
                [new SKColor(0x3A, 0x3A, 0x3A), new SKColor(0x17, 0x17, 0x17)],
                SKShaderTileMode.Clamp),
        };

        canvas.DrawRect(0, 0, width, height, paint);
    }

    private void DrawBlackPlate(SKCanvas canvas, SKRect plate, int seed, Random rng)
    {
        var s = style.Plate;
        var plateColor = StyleColor.Parse(style.Palette.Plate);

        using var shadowPaint = new SKPaint
        {
            ImageFilter = SKImageFilter.CreateDropShadow(0, s.ShadowOffsetY, s.ShadowBlur, s.ShadowBlur,
                new SKColor(0, 0, 0, (byte)Math.Clamp(s.ShadowAlpha, 0, 255))),
        };

        canvas.SaveLayer(shadowPaint);

        using (var paint = new SKPaint { Color = plateColor, IsAntialias = true })
        using (var path = Grunge.BrushStrokePath(plate, seed,
                   edgeAmp: plate.Height * s.EdgeAmpFactor, endAmp: plate.Height * s.EndAmpFactor))
        {
            canvas.DrawPath(path, paint);
        }

        Grunge.BrushTeeth(canvas, plate.Left, plate.Top, plate.Bottom, -1, plateColor, rng,
            plate.Height * s.TeethLeftFactor);
        Grunge.BrushTeeth(canvas, plate.Right, plate.Top, plate.Bottom, +1, plateColor, rng,
            plate.Height * s.TeethRightFactor);

        // сухая неравномерность краски + мелкая крошка по краям
        Grunge.Mottle(canvas, plate, rng, StyleColor.Parse(s.MottleColor),
            count: s.MottleCount, maxRadius: plate.Height * s.MottleMaxRadiusFactor);
        Grunge.Distress(canvas, plate, rng, s.DistressDensity);

        canvas.Restore();
    }

    /// <summary>Зелёная подмазка за номером: рваный мазок с тяжёлым тёмным мотлингом —
    /// в оригинале выглядит как полустёртая зелёная краска под цифрами и левее плашки.</summary>
    private void DrawGreenSmear(SKCanvas canvas, SKRect area, int seed, Random rng)
    {
        var s = style.Smear;
        var smearColor = StyleColor.Parse(style.Palette.Smear);

        canvas.SaveLayer(null);

        using (var paint = new SKPaint { Color = smearColor, IsAntialias = true })
        {
            using (var path = Grunge.BrushStrokePath(area, seed + 31,
                       edgeAmp: area.Height * s.EdgeAmpFactor, endAmp: area.Height * s.EndAmpFactor))
            {
                canvas.DrawPath(path, paint);
            }

            // второй клочковатый мазок у левого торца — рваная масса, а не ровные полосы
            var clump = new SKRect(area.Left - area.Height * 0.25f, area.Top + area.Height * 0.15f,
                area.Left + area.Width * 0.45f, area.Bottom + area.Height * 0.10f);

            using (var clumpPath = Grunge.BrushStrokePath(clump, seed + 47,
                       edgeAmp: clump.Height * 0.14f, endAmp: clump.Height * 0.22f))
            {
                canvas.DrawPath(clumpPath, paint);
            }
        }

        Grunge.BrushTeeth(canvas, area.Left, area.Top, area.Bottom, -1, smearColor, rng,
            area.Height * s.TeethFactor);

        // тёмная мотлинг-фактура почти съедает мазок — остаются клочья зелени
        Grunge.Mottle(canvas, area, rng, StyleColor.Parse(s.MottleColor),
            count: (int)(area.Width * area.Height / 260f * s.MottleDensity),
            maxRadius: area.Height * s.MottleMaxRadiusFactor);
        Grunge.Distress(canvas, area, rng, s.DistressDensity);

        canvas.Restore();
    }

    private void DrawChipsAndSplatter(SKCanvas canvas, SKRect plate, Random rng)
    {
        var accents = style.Accents;
        var chipsColor = StyleColor.Parse(style.Palette.Chips);

        // квадратные чипсы вдоль верхней кромки (гуще к концам) и зелёная крошка на правой части плашки
        Grunge.ChipsAlongLine(canvas, new SKPoint(plate.Left - 10, plate.Top - 4),
            new SKPoint(plate.Left + plate.Width * 0.35f, plate.Top - 4), chipsColor, rng,
            count: accents.ChipsLeftCount, spread: accents.ChipsSpread);
        Grunge.ChipsAlongLine(canvas, new SKPoint(plate.Right - plate.Width * 0.25f, plate.Top - 4),
            new SKPoint(plate.Right + 14, plate.Top + 2), chipsColor, rng,
            count: accents.ChipsRightCount, spread: accents.ChipsSpread);

        Grunge.Splatter(canvas, new SKPoint(plate.Right - plate.Width * 0.08f, plate.MidY), chipsColor, rng,
            count: accents.SplatterGreenCount, spread: plate.Height * 0.45f);
        Grunge.Splatter(canvas, new SKPoint(plate.Left - 24, plate.Bottom - 8),
            StyleColor.Parse(style.Palette.DarkSplatter), rng,
            count: accents.SplatterDarkCount, spread: plate.Height * 0.40f);
    }

    /// <summary>Зелёная полоса-мазок под подзаголовок: правый торец рваный сильнее (длинные «зубья»).</summary>
    private void DrawGreenBand(SKCanvas canvas, SKRect band, int seed, Random rng)
    {
        var s = style.Band;
        var bandTop = StyleColor.Parse(style.Palette.BandTop);
        var bandBottom = StyleColor.Parse(style.Palette.BandBottom);

        canvas.SaveLayer(null);

        using (var paint = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, band.Top), new SKPoint(0, band.Bottom),
                [bandTop, bandBottom],
                SKShaderTileMode.Clamp),
        })
        using (var path = Grunge.BrushStrokePath(band, seed + 57,
                   edgeAmp: band.Height * s.EdgeAmpFactor, endAmp: band.Height * s.EndAmpFactor))
        {
            canvas.DrawPath(path, paint);
        }

        Grunge.BrushTeeth(canvas, band.Left, band.Top, band.Bottom, -1, bandBottom, rng,
            band.Height * s.TeethLeftFactor);
        Grunge.BrushTeeth(canvas, band.Right, band.Top, band.Bottom, +1, bandTop, rng,
            band.Height * s.TeethRightFactor);

        Grunge.Mottle(canvas, band, rng, StyleColor.Parse(s.MottleColor),
            count: (int)(band.Width / 9f * s.MottleDensity), maxRadius: band.Height * s.MottleMaxRadiusFactor);
        Grunge.Distress(canvas, band, rng, s.DistressDensity);

        canvas.Restore();
    }

    private void DrawTitleRow(SKCanvas canvas, string? prefix, string main, SKFont font,
        float left, float baseline, float spaceWidth, float size, Random rng)
    {
        var palette = style.Palette;
        var x = left;

        if (prefix is not null)
        {
            var prefixWidth = font.MeasureText(prefix, out var prefixBounds);
            Draw3DText(canvas, prefix, font, x, baseline, prefixBounds, size, rng,
                faceColors:
                [
                    StyleColor.Parse(palette.GreenFaceTop),
                    StyleColor.Parse(palette.GreenFaceMid),
                    StyleColor.Parse(palette.GreenFaceBottom),
                ],
                extrudeColor: StyleColor.Parse(palette.GreenExtrude),
                mottleColor: StyleColor.Parse(style.TitleFx.PrefixMottleColor));
            x += prefixWidth + spaceWidth;
        }

        font.MeasureText(main, out var mainBounds);
        Draw3DText(canvas, main, font, x, baseline, mainBounds, size, rng,
            faceColors:
            [
                StyleColor.Parse(palette.WhiteFaceTop),
                StyleColor.Parse(palette.WhiteFaceMid),
                StyleColor.Parse(palette.WhiteFaceBottom),
            ],
            extrudeColor: StyleColor.Parse(palette.WhiteExtrude),
            mottleColor: StyleColor.Parse(style.TitleFx.MainMottleColor));
    }

    /// <summary>Экструдированный текст с фактурой: глубина → обводка → градиентная грань →
    /// мотлинг по буквам → мелкие выгрызы. Всё в SaveLayer, чтобы фактура не задела фон.</summary>
    private void Draw3DText(SKCanvas canvas, string text, SKFont font, float x, float baseline,
        SKRect bounds, float size, Random rng, SKColor[] faceColors, SKColor extrudeColor, SKColor mottleColor)
    {
        var fx = style.TitleFx;
        var depth = MathF.Max(4f, size * fx.ExtrudeDepthFactor);

        canvas.SaveLayer(null);

        using (var extrude = new SKPaint { Color = extrudeColor, IsAntialias = true })
        {
            var steps = (int)MathF.Ceiling(depth);

            for (var i = steps; i >= 1; i--)
                canvas.DrawText(text, x + i * fx.ExtrudeStepX, baseline + i, SKTextAlign.Left, font, extrude);
        }

        using (var stroke = new SKPaint
        {
            Color = StyleColor.Parse(style.Palette.TitleStroke),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = size * fx.StrokeWidthFactor,
            StrokeJoin = SKStrokeJoin.Round,
        })
        {
            canvas.DrawText(text, x, baseline, SKTextAlign.Left, font, stroke);
        }

        using (var fill = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, baseline + bounds.Top), new SKPoint(0, baseline),
                faceColors, [0f, Math.Clamp(fx.FaceGradientMidStop, 0.05f, 0.95f), 1f],
                SKShaderTileMode.Clamp),
        })
        {
            canvas.DrawText(text, x, baseline, SKTextAlign.Left, font, fill);
        }

        var textRect = new SKRect(x + bounds.Left, baseline + bounds.Top,
            x + bounds.Right + depth, baseline + depth);

        Grunge.Mottle(canvas, textRect, rng, mottleColor,
            count: (int)(textRect.Width * textRect.Height / 480f * fx.MottleDensity),
            maxRadius: size * fx.MottleMaxRadiusFactor);
        Grunge.Distress(canvas, textRect, rng, fx.DistressDensity);

        canvas.Restore();
    }

    /// <summary>Подзаголовок — чёрный текст на зелёной полосе (как в оригинале), слегка потёртый.</summary>
    private void DrawSubtitle(SKCanvas canvas, string subtitle, SKFont font, float centerX, float baseline,
        float width, SKRect bounds, Random rng)
    {
        canvas.SaveLayer(null);

        using (var fill = new SKPaint { Color = StyleColor.Parse(style.Palette.SubtitleText), IsAntialias = true })
        {
            canvas.DrawText(subtitle, centerX, baseline, SKTextAlign.Center, font, fill);
        }

        var textRect = new SKRect(centerX - width / 2, baseline + bounds.Top, centerX + width / 2, baseline + bounds.Bottom);
        Grunge.Distress(canvas, textRect, rng, style.Band.SubtitleDistressDensity);

        canvas.Restore();
    }

    /// <summary>Третья строка (ник/дата/тег) под подзаголовком — слегка потёртый текст
    /// (<see cref="BannerStyle.TaglineStyle"/>). Рисуется внутри наклонённого блока.</summary>
    private void DrawTagline(SKCanvas canvas, string tagline, SKTypeface typeface, float centerX,
        float subBaseline, float titleSize, Random rng)
    {
        var t = style.Tagline;
        using var font = new SKFont(typeface, titleSize * Math.Clamp(t.SizeFactor, 0.1f, 1f))
        {
            ScaleX = t.ScaleX,
            Subpixel = true,
        };

        var baseline = subBaseline + titleSize * t.BaselineGapFactor;
        var width = font.MeasureText(tagline, out var bounds);

        canvas.SaveLayer(null);
        using (var fill = new SKPaint { Color = StyleColor.Parse(t.Color), IsAntialias = true })
        {
            canvas.DrawText(tagline, centerX, baseline, SKTextAlign.Center, font, fill);
        }

        var rect = new SKRect(centerX - width / 2, baseline + bounds.Top, centerX + width / 2, baseline + bounds.Bottom);
        Grunge.Distress(canvas, rect, rng, t.DistressDensity);
        canvas.Restore();
    }
}
